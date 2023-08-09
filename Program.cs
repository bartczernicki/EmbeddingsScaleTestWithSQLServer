using System.IO.Compression;
using System.Net;
using System.Text;
using System.Net.Http;
using System;
using Newtonsoft.Json;
using System.IO;
using System.Collections.Generic;
using SharpToken;

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

            var totalTokenLength = 0;
            List<string> passages = new List<string>(200000);
            using (FileStream fileStream = new FileStream(wikipediaFilepath, FileMode.Open, FileAccess.Read))
            using (GZipStream compressionStream = new GZipStream(fileStream, CompressionMode.Decompress))
            using (StreamReader streamReader = new StreamReader(compressionStream, Encoding.UTF8))
            {
                string? line; // Declare line as nullable
                while ((line = streamReader.ReadLine()) != null)
                {
                    var data = JsonConvert.DeserializeObject<dynamic>(line.Trim());

                    // Only add the first paragraph
                    string? paragraph = null;
                    if (data?["paragraphs"] != null && data?["paragraphs"].HasValues && data?["paragraphs"][0] != null)
                    {
                        paragraph = data["paragraphs"][0].ToString();
                    }
                    //var paragraph = data["paragraphs"][0].ToString();

                    if (paragraph != null)
                    {
                        passages.Add(paragraph);
                    }

                    // Return the optimal text encodings, this is if tokens can be split perfect (no overlap)
                    var encodedTokens = cl100kBaseEncoding.Encode(paragraph);
                    totalTokenLength += encodedTokens.Count;
                }
            }

            Console.WriteLine("Wikipedia Passages Count: " + passages.Count.ToString("N0"));
            Console.WriteLine("Embeddings Tokens Count: " + totalTokenLength.ToString("N0"));
            Console.WriteLine("Embeddings Tokens OpenAI Processing Cost: " + string.Format("{0:C}", totalTokenLength * 0.0001/1000));
        }
    }
}
