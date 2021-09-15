using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace VODSearch
{
    class Program
    {
        static readonly Regex linkRegex = new Regex(@"\b(?:https?://|www\.)\S+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static string cacheDirectory = "cache";

        static void Main(string[] args)
        {
            string file = "vods.txt";
            string term = "";

            using var webClient = new WebClient();
            string content = File.ReadAllText(file);

            if (!Directory.Exists(cacheDirectory))
                Directory.CreateDirectory(cacheDirectory);

            Dictionary<string, int> counts = new Dictionary<string, int>();
            foreach (Match m in linkRegex.Matches(content))
            {
                var uri = new Uri(m.Value);
                var query = HttpUtility.ParseQueryString(uri.Query);
                var jsonParam = query.Get("json");
                if (jsonParam != null)
                {
                    var jsonUri = new Uri(jsonParam);
                    Search(webClient, term, jsonUri, counts).Wait();
                }
            }

            Console.WriteLine("== \"{0}\" count: {1} ==", term, counts.Values.Sum());
            foreach(var kp in counts.OrderByDescending(kp => kp.Value))
            {
                Console.WriteLine("|{0, 25}|{1, 5}|", kp.Key, kp.Value);
            }
        }

        static async Task Search(WebClient client, string term, Uri uri, Dictionary<string, int> counts)
        {
            string cacheName = uri.LocalPath.Trim('/');
            string cacheFilePath = Path.Combine(cacheDirectory, cacheName);

            Console.ForegroundColor = ConsoleColor.Gray;
            if (!File.Exists(cacheFilePath))
            {
                Console.WriteLine("Downloading {0}", uri);
                await client.DownloadFileTaskAsync(uri, cacheFilePath);
            }
            try
            {
                await SearchFile(term, cacheFilePath, counts);
            }
            catch (JsonReaderException e)
            {
                Console.WriteLine("Deleted cached filed: {0}", cacheFilePath);
                File.Delete(cacheFilePath);

                // Try again
                await Search(client, term, uri, counts);
            }
            Console.ResetColor();
        }

        static async Task SearchFile(string term, string file, Dictionary<string, int> counts)
        {
            //Console.WriteLine("Searching {0}", file);
            string content = await File.ReadAllTextAsync(file);
            if (content.Contains(term))
            {
                Console.WriteLine("Term Found: {0}", file);
                if (Path.GetExtension(file) == ".json")
                {
                    await SearchMessages(term, content, counts);
                    Console.WriteLine("");
                }
            }
        }

        static Task SearchMessages(string term, string json, Dictionary<string, int> counts )
        {
            var obj = Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(json);
            foreach (var comment in obj["comments"])
            {
                var message = comment["message"];
                if (message == null) continue;

                var body = message["body"];
                if (body.Value<string>().Contains(term))
                {
                    string name = "N/A";
                    if (comment["commenter"] != null)
                    {
                        name = comment["commenter"]["name"].Value<string>();
                    }

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write("{0}: ", name);

                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine(body);

                    // Add count
                    if (!counts.ContainsKey(name))
                        counts.Add(name, 0);

                    counts[name] += 1;
                }
            }

            return Task.CompletedTask;
        }
    }
}
