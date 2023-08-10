using EmbeddingsScaleTestWithSQLServer.Classes;
using Newtonsoft.Json;
using SharpToken;
using System.IO.Compression;
using System.Text;

namespace EmbeddingsScaleTestWithSQLServer
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var wikipediaFilepath = System.IO.Path.Combine(Environment.CurrentDirectory, "simplewiki-2020-11-01.jsonl.gz");
            Console.WriteLine("File Path: " + wikipediaFilepath);

            // Get the encoding for text-embedding-ada-002
            var cl100kBaseEncoding = GptEncoding.GetEncoding("cl100k_base");

            if (!File.Exists(wikipediaFilepath))
            {
                using (HttpClient httpClient = new HttpClient())
                {
                    using (HttpResponseMessage response = await httpClient.GetAsync("http://sbert.net/datasets/simplewiki-2020-11-01.jsonl.gz", HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode(); // Throws an exception if the request was not successful

                        using (Stream contentStream = await response.Content.ReadAsStreamAsync(), fileStream = new FileStream(wikipediaFilepath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            await contentStream.CopyToAsync(fileStream);
                            fileStream.Close();
                        }
                    }
                }
            }


            int totalTokenLength = 0, totalCharactersLength = 0, maxTokensInSingleParagraph = 0;
            List<string> passages = new List<string>(200000);
            using (FileStream fileStream = new FileStream(wikipediaFilepath, FileMode.Open, FileAccess.Read))
            using (GZipStream compressionStream = new GZipStream(fileStream, CompressionMode.Decompress))
            using (StreamReader streamReader = new StreamReader(compressionStream, Encoding.UTF8))
            {
                string? line; // Declare line as nullable
                while ((line = streamReader.ReadLine()) != null)
                {
                    var simpleWikiData = JsonConvert.DeserializeObject<SimpleWiki>(line.Trim());

                    if (simpleWikiData != null)
                    {
                        foreach (var paragraph in simpleWikiData.paragraphs)
                        {
                            passages.Add(paragraph);

                            // Return the optimal text encodings, this is if tokens can be split perfect (no overlap)
                            var encodedTokens = cl100kBaseEncoding.Encode(paragraph);
                            maxTokensInSingleParagraph = (encodedTokens.Count > maxTokensInSingleParagraph) ? encodedTokens.Count : maxTokensInSingleParagraph;
                            totalTokenLength += encodedTokens.Count;
                            totalCharactersLength += paragraph.Length;
                        }
                    }
                }
            }

            Console.WriteLine("Wikipedia Paragraphs Count: " + passages.Count.ToString("N0"));
            Console.WriteLine("Max Tokens in a single paragraph: " + maxTokensInSingleParagraph.ToString("N0"));
            Console.WriteLine("Total Text Characters Processed: " + totalCharactersLength.ToString("N0"));
            Console.WriteLine("Total Text (OpenAI) Tokens Processed: " + totalTokenLength.ToString("N0"));
            Console.WriteLine("Total Text (OpenAI) Tokens Processing Cost: " + string.Format("{0:C}", totalTokenLength * 0.0001/1000));
        }
    }
}
