using EmbeddingsScaleTestWithSQLServer.Classes;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SharpToken;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO.Compression;
using System.Text;
using System.Transactions;
using System.Windows.Input;

namespace EmbeddingsScaleTestWithSQLServer
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            ConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
            IConfiguration configuration = configurationBuilder.AddUserSecrets<Program>().Build();
            string? connectionString = configuration.GetSection("SQL")["SqlConnection"];

            var wikipediaFilepath = System.IO.Path.Combine(Environment.CurrentDirectory, "simplewiki-2020-11-01.jsonl.gz");
            Console.WriteLine("File Path: " + wikipediaFilepath);

            // Get the encoding for text-embedding-ada-002
            var MAXTOKENSPERLINE = 220;
            var MAXTOKENSPERPARAGRAPH = 500;
            var cl100kBaseEncoding = GptEncoding.GetEncoding("cl100k_base");

            var currentSQLScriptsFolder = System.IO.Path.Combine(Environment.CurrentDirectory, "SQL");
            var sqlScriptsFilePath = Path.Combine(currentSQLScriptsFolder, "SQLScripts.sql");
            var scriptText = File.ReadAllText(sqlScriptsFilePath);

            // SQL scripts that are multi-command split by GO
            var sqlCommandsInScripts = scriptText.Split(new[] { "GO", "Go", "go" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var commandText in sqlCommandsInScripts)
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    using (SqlCommand command = new SqlCommand(string.Empty, connection))
                    {
                        // This is just executing SQL scripts, so timeout should be relatively low
                        command.CommandTimeout = 100; // seconds
                        command.CommandText = commandText;
                        await command.ExecuteNonQueryAsync();
                    }

                    await connection.CloseAsync();
                }
            }


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
            ConcurrentBag<string> passages = new ConcurrentBag<string>();
            using (FileStream fileStream = new FileStream(wikipediaFilepath, FileMode.Open, FileAccess.Read))
            using (GZipStream compressionStream = new GZipStream(fileStream, CompressionMode.Decompress))
            using (StreamReader streamReader = new StreamReader(compressionStream, Encoding.UTF8))
            {
                string? line; // Declare line as nullable
                while ((line = await streamReader.ReadLineAsync()) != null)
                {
                    var simpleWikiData = JsonConvert.DeserializeObject<SimpleWiki>(line.Trim());

                    if (simpleWikiData != null)
                    {
                        // Concat the paragraphs into single string
                        var concatenatedParagraphs = String.Join(' ', simpleWikiData.paragraphs);
                        var concatenatedParagraphTokens = cl100kBaseEncoding.Encode(concatenatedParagraphs);
                        totalTokenLength += concatenatedParagraphTokens.Count;
                        maxTokensInSingleParagraph = (concatenatedParagraphs.Length > maxTokensInSingleParagraph) ? concatenatedParagraphs.Length : maxTokensInSingleParagraph;

                        // Split this using Semantic Kernel
                        var paragraphsWithSemanticKernel = new List<string>();
                        if (concatenatedParagraphTokens.Count > MAXTOKENSPERPARAGRAPH)
                        {
                            var paragraphLines = Microsoft.SemanticKernel.Text.TextChunker.SplitPlainTextLines(concatenatedParagraphs, MAXTOKENSPERLINE);
                            paragraphsWithSemanticKernel = Microsoft.SemanticKernel.Text.TextChunker.SplitPlainTextParagraphs(paragraphLines, MAXTOKENSPERPARAGRAPH, overlapTokens: 0);
                        }

                        using (SqlConnection connection = new SqlConnection(connectionString))
                        {
                            await connection.OpenAsync();

                            // Insert first Wiki record - WikiID and Title
                            using (SqlCommand command = new SqlCommand(string.Empty, connection))
                            {
                                command.CommandText = "INSERT INTO WikipediaPassages(WikiId, Title) VALUES(@wikiId, @title)";
                                command.Parameters.AddWithValue("@wikiId", simpleWikiData.id);
                                command.Parameters.AddWithValue("@title", simpleWikiData.title);
                                command.CommandTimeout = 30; // seconds
                                await command.ExecuteNonQueryAsync();
                            }

                            // Insert Wiki record - Paragraphs
                            // create a transaction to insert all paragraphs at once
                            using (SqlTransaction insertTransaction = connection.BeginTransaction())
                            {
                                using (SqlCommand command = new SqlCommand(string.Empty, connection))
                                {
                                    command.Transaction = insertTransaction;
                                    command.CommandTimeout = 60; // seconds
                                    command.CommandText = "INSERT INTO WikipediaPassagesParagraphs(WikiId, ParagraphId, Paragraph) VALUES(@wikiId, @paragraphId, @paragraph)";
                                    command.Parameters.Add(new SqlParameter("@wikiId", SqlDbType.Int));
                                    command.Parameters.Add(new SqlParameter("@paragraphId", SqlDbType.Int));
                                    command.Parameters.Add(new SqlParameter("@paragraph", SqlDbType.VarChar, 3000));

                                    try
                                    {
                                        // If paragraphs can be combined less than token max, combine them
                                        if (concatenatedParagraphTokens.Count < MAXTOKENSPERPARAGRAPH)
                                        {
                                            totalCharactersLength += concatenatedParagraphs.Length;
                                            passages.Add(concatenatedParagraphs);
                                            command.Parameters[0].Value = simpleWikiData.id;
                                            command.Parameters[1].Value = 1;
                                            command.Parameters[2].Value = concatenatedParagraphs;
                                            if (await command.ExecuteNonQueryAsync() != 1)
                                            {
                                                //'handled as needed, 
                                                //' but this snippet will throw an exception to force a rollback
                                                throw new InvalidProgramException();
                                            }
                                        }
                                        else
                                        {
                                            var paragraphCount = 0;
                                            foreach (var paragraph in paragraphsWithSemanticKernel)
                                            {
                                                paragraphCount++;
                                                totalCharactersLength += paragraph.Length;
                                                passages.Add(paragraph);

                                                command.Parameters[0].Value = simpleWikiData.id;
                                                command.Parameters[1].Value = paragraphCount;
                                                command.Parameters[2].Value = paragraph;
                                                if (await command.ExecuteNonQueryAsync() != 1)
                                                {
                                                    //'handled as needed, 
                                                    //' but this snippet will throw an exception to force a rollback
                                                    throw new InvalidProgramException();
                                                }
                                            }
                                        }

                                        await insertTransaction.CommitAsync();
                                    }
                                    catch (Exception)
                                    {
                                        insertTransaction.Rollback();
                                        throw;
                                    }
                                }
                            }

                            await connection.CloseAsync();
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
