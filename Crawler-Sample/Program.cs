using AngleSharp;
using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CrawlerSample
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string host = "https://www.basketball-reference.com";
            string souce = "https://www.basketball-reference.com/players/"; // 目標網頁

            HttpClient httpClient = new HttpClient();
            var responseMessage = await httpClient.GetAsync(souce); // 發送目標網頁請求

            //檢查回應的伺服器狀態StatusCode是否是200 OK
            if (responseMessage.StatusCode == System.Net.HttpStatusCode.OK)
            {
                string response = responseMessage.Content.ReadAsStringAsync().Result; // 取得目標網頁內容

                // 使用AngleSharp時的前置設定
                var config = Configuration.Default;
                var context = BrowsingContext.New(config);

                // 將httpclient拿到的資料放入res.Content中())
                var document = await context.OpenAsync(res => res.Content(response));

                // 找出class="page_index" > li > a的元素
                var links = document.QuerySelectorAll(".page_index>li>a"); // 取得A-Z的網頁列表
                Dictionary<string, string> linkDict = links.ToDictionary(x => x.TextContent, x => x.Attributes["href"]?.Value); // 取得檔案名稱與連結

                Stopwatch sw = new Stopwatch();
                sw.Start();
                // 取得A-Z球員數據並匯出csv
                GetPlayerListAsync(host, linkDict).Wait();
                sw.Stop();

                Console.WriteLine($"總共花費時間: {sw.ElapsedMilliseconds} ms");
            }

            Console.ReadKey();
        }

        /// <summary>
        /// 取得A-Z球員清單內容
        /// </summary>
        /// <returns></returns>
        public static async Task GetPlayerListAsync(string host, Dictionary<string, string> linkDict)
        {
            List<Task> tasks = new List<Task>();
            foreach (var link in linkDict)
            {
                tasks.Add(Task.Run(async () =>
                {
                    HttpClient httpClient = new HttpClient();
                    // 使用AngleSharp時的前置設定
                    var config = Configuration.Default;
                    var context = BrowsingContext.New(config);

                    var result = new List<Data>();

                    // 發送A-Z球員清單請求
                    var listMessage = await httpClient.GetAsync(host + link.Value);

                    // 檢查回應的伺服器狀態StatusCode是否是200 OK
                    if (listMessage.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        string listResponse = listMessage.Content.ReadAsStringAsync().Result; // 取得A-Z球員清單內容
                        if (String.IsNullOrEmpty(listResponse)) return;

                        // 將httpclient拿到的資料放入res.Content中())
                        var listDocument = await context.OpenAsync(res => res.Content(listResponse));

                        // 找出tbody > tr > th > a的所有元素
                        var listLinks = listDocument.QuerySelectorAll("tbody>tr>th>a").OrderBy(x => x.TextContent).ToList();  // 取得球員數據網址，並按照球員名稱排序
                        foreach (var listLink in listLinks.Select((value, index) => new { value, index }))
                        {
                            var data = new Data
                            {
                                Player = listLink.value.TextContent
                            };

                            // 發送球員數據請求
                            var playerMessage = await httpClient.GetAsync(host + listLink.value.Attributes["href"]?.Value);
                            // 檢查回應的伺服器狀態StatusCode是否是200 OK
                            if (playerMessage.StatusCode == System.Net.HttpStatusCode.OK)
                            {
                                string playerResponse = playerMessage.Content.ReadAsStringAsync().Result; // 取得球員數據內容
                                if (String.IsNullOrEmpty(playerResponse)) continue;

                                // 將httpclient拿到的資料放入res.Content中())
                                var playerDocument = await context.OpenAsync(res => res.Content(playerResponse));

                                // 字母 第幾個/總數:球員名稱 球員資訊連結
                                Console.WriteLine($"  {link.Key} {listLink.index}/{listLinks.Count}:{listLink.value.TextContent}  {listLink.value.Attributes["href"]?.Value}");

                                // 找出球員的生涯數據
                                var playerSummary = playerDocument.QuerySelector(".stats_pullout").QuerySelectorAll("div[class]").SelectMany(x => x.QuerySelectorAll("div")).ToDictionary(h4 => h4.QuerySelector("h4").TextContent, p => p.QuerySelectorAll("p").FirstOrDefault(x => !String.IsNullOrEmpty(x.TextContent)).TextContent);
                                foreach (var summary in playerSummary)
                                {
                                    Console.WriteLine($"  {summary.Key} : {summary.Value}");
                                    // 依照h4元素判斷數據所屬
                                    switch (summary.Key)
                                    {
                                        case "G":
                                            data.G = (summary.Value == "-") ? 0 : Convert.ToDecimal(summary.Value);
                                            break;
                                        case "PTS":
                                            data.PTS = (summary.Value == "-") ? 0 : Convert.ToDecimal(summary.Value);
                                            break;
                                        case "TRB":
                                            data.TRB = (summary.Value == "-") ? 0 : Convert.ToDecimal(summary.Value);
                                            break;
                                        case "AST":
                                            data.AST = (summary.Value == "-") ? 0 : Convert.ToDecimal(summary.Value);
                                            break;
                                        case "FG%":
                                            data.FG = (summary.Value == "-") ? 0 : Convert.ToDecimal(summary.Value);
                                            break;
                                        case "FG3%":
                                            data.FG3 = (summary.Value == "-") ? 0 : Convert.ToDecimal(summary.Value);
                                            break;
                                        case "FT%":
                                            data.FT = (summary.Value == "-") ? 0 : Convert.ToDecimal(summary.Value);
                                            break;
                                        case "eFG%":
                                            data.EFG = (summary.Value == "-") ? 0 : Convert.ToDecimal(summary.Value);
                                            break;
                                        case "PER":
                                            data.PER = (summary.Value == "-") ? 0 : Convert.ToDecimal(summary.Value);
                                            break;
                                        case "WS":
                                            data.WS = (summary.Value == "-") ? 0 : Convert.ToDecimal(summary.Value);
                                            break;
                                    }
                                }
                                result.Add(data);
                            }
                        }
                        // 依照字母匯出csv檔案
                        WriteToCsv(link.Key, result);
                    }
                }));
            }
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 匯出csv檔案
        /// </summary>
        public static void WriteToCsv(string fileName, List<Data> result)
        {
            // 依照字母匯出csv檔案
            using var writer = new StreamWriter($"{Environment.CurrentDirectory}/{fileName}.csv", false, new UTF8Encoding(true));
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            csv.Configuration.RegisterClassMap<GetExportMap>();
            csv.WriteHeader(typeof(Data));
            csv.NextRecord();
            csv.WriteRecords(result);
        }

        /// <summary>
        /// 球員生涯資料
        /// </summary>
        public class Data
        {
            public string Player { get; set; }
            public decimal G { get; set; }
            public decimal PTS { get; set; }
            public decimal TRB { get; set; }
            public decimal AST { get; set; }
            public decimal FG { get; set; }
            public decimal FG3 { get; set; }
            public decimal FT { get; set; }
            public decimal EFG { get; set; }
            public decimal PER { get; set; }
            public decimal WS { get; set; }

        }

        /// <summary>
        /// 匯出檔案欄位對應
        /// </summary>
        public class GetExportMap : ClassMap<Data>
        {
            /// <summary>
            /// 對應欄位
            /// </summary>
            public GetExportMap()
            {
                Map(m => m.Player).Name("Player");
                Map(m => m.G).Name("G");
                Map(m => m.PTS).Name("PTS");
                Map(m => m.TRB).Name("TRB");
                Map(m => m.AST).Name("AST");
                Map(m => m.FG).Name("FG(%)");
                Map(m => m.FG3).Name("FG3(%)");
                Map(m => m.FT).Name("FT(%)");
                Map(m => m.EFG).Name("eFG(%)");
                Map(m => m.PER).Name("PER");
                Map(m => m.WS).Name("WS");
            }
        }
    }
}
