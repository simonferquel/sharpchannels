using HtmlAgilityPack;
using SharpChannels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebCrawler.Simple
{
    class Program
    {
        struct Html
        {
            public string Url;
            public string Source;
        }

        static async Task UnviversalWorker(Channel<Html> htmls, Channel<IEnumerable<string>> urls, HashSet<string> seenUrls, Channel<bool> cancelChannel)
        {
            using (var countDown = new CountdownEvent(1))
            using (var client = new HttpClient())
            {
                var cts = new CancellationTokenSource();
                while (true)
                {
                    bool canceled = false;
                    await new Multiplex()
                        .CaseReceive(cancelChannel, _ => canceled = true)
                        .CaseReceive(htmls, async html =>
                        {
                            countDown.AddCount();
                            try
                            {
                                await Parse(html, urls, seenUrls, cancelChannel);
                            }
                            finally { countDown.Signal(); }
                        })
                        .CaseReceive(urls, async url =>
                        {
                            countDown.AddCount();
                            try
                            {
                                await Fetch(client, url, htmls, cancelChannel, cts.Token);
                            }
                            finally { countDown.Signal(); }
                        });
                    if (canceled)
                    {
                        cts.Cancel();
                        break;
                    }
                }
                countDown.Signal();
                countDown.Wait();

            }
        }

        static async Task Fetch(HttpClient client, IEnumerable<string> urls, Channel<Html> outputHtml, Channel<bool> cancelChannel, CancellationToken cancelToken)
        {
            foreach (var url in urls)
            {
                if (url == null)
                {
                    return;
                }
                Console.WriteLine("[Fetching] Begin " + url);
                try
                {
                    var response = await client.GetAsync(url, cancelToken);
                    response.EnsureSuccessStatusCode();
                    var html = await response.Content.ReadAsStringAsync();

                    Console.WriteLine("[Fetching] End " + url);
                    await new Multiplex()
                        .CaseSend(outputHtml, new Html { Source = html, Url = url })
                        .CaseReceive(cancelChannel, _ => { });

                }
                catch (OperationCanceledException) { }
                catch
                {

                    Console.WriteLine("[Fetching] Failed " + url);
                }
                if (cancelChannel.IsClosed)
                {
                    return;
                }
            }

        }
        static async Task Parse(Html html, Channel<IEnumerable<string>> outputUrls, HashSet<string> seenUrls, Channel<bool> cancelChannel)
        {

            try
            {
                Console.WriteLine("[Parser] begin " + html.Url);
                var baseUri = new Uri(html.Url);
                var doc = new HtmlDocument();
                doc.LoadHtml(html.Source);
                List<string> output = new List<string>();
                foreach (var url in doc.DocumentNode.DescendantNodes()
                    .Where(n => n.Name == "a")
                    .Select(n => n.GetAttributeValue("href", (string)null))
                    .Where(u => u != null))
                {
                    try
                    {
                        var fixedUrl = url;
                        if (Uri.IsWellFormedUriString(fixedUrl, UriKind.Relative))
                        {
                            fixedUrl = new Uri(baseUri, fixedUrl).ToString();
                        }
                        if (!Uri.IsWellFormedUriString(fixedUrl, UriKind.Absolute))
                        {
                            continue;
                        }
                        lock (seenUrls)
                        {
                            if (!seenUrls.Add(fixedUrl))
                            {
                                continue;
                            }
                        }
                        output.Add(fixedUrl);
                    }
                    catch { }
                }
                await new Multiplex()
                    .CaseSend(outputUrls, output)
                    .CaseReceive(cancelChannel, _ => { });
                if (cancelChannel.IsClosed)
                {
                    return;
                }

                Console.WriteLine("[Parser] end " + html.Url);
            }
            catch { }

        }


        static void Main(string[] args)
        {
            var urls = new Channel<IEnumerable<string>>(50);
            var htmls = new Channel<Html>(50);
            var cancel = new Channel<bool>();
            var seen = new HashSet<string>();
            seen.Add(args[0]);
            var pipeline = Task.WhenAll(
                Task.Run(() => UnviversalWorker(htmls, urls, seen, cancel)),
                Task.Run(() => UnviversalWorker(htmls, urls, seen, cancel)),
                Task.Run(() => UnviversalWorker(htmls, urls, seen, cancel)),
                Task.Run(() => UnviversalWorker(htmls, urls, seen, cancel)),
                Task.Run(() => UnviversalWorker(htmls, urls, seen, cancel)),
                Task.Run(() => UnviversalWorker(htmls, urls, seen, cancel)),
                Task.Run(() => UnviversalWorker(htmls, urls, seen, cancel)),
                Task.Run(() => UnviversalWorker(htmls, urls, seen, cancel)),
                Task.Run(() => UnviversalWorker(htmls, urls, seen, cancel)),
                Task.Run(() => UnviversalWorker(htmls, urls, seen, cancel)),
                Task.Run(() => UnviversalWorker(htmls, urls, seen, cancel)),
                Task.Run(() => UnviversalWorker(htmls, urls, seen, cancel)),
                Task.Run(() => UnviversalWorker(htmls, urls, seen, cancel)),
                Task.Run(() => UnviversalWorker(htmls, urls, seen, cancel)));
            urls.TrySend(new[] { args[0] });
            Console.WriteLine("Press any key to cancel");
            Console.ReadLine();
            cancel.Close();
            pipeline.Wait();
        }
    }
}
