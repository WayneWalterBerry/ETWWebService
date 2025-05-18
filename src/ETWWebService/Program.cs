using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ETWWebService
{
    internal class Program
    {
        private static readonly string Url = "http://localhost:5000/";
        private const string DefaultProvider = "Microsoft-Windows-Kernel-Process";

        static async Task Main(string[] args)
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add(Url);

            try
            {
                listener.Start();
                Console.WriteLine($"Listening for requests at {Url}");
                Console.WriteLine("Usage: http://localhost:5000/?[ETWProviderName]");
                Console.WriteLine("Example: http://localhost:5000/?Microsoft-Windows-Kernel-Process");
                Console.WriteLine("Press Ctrl+C to exit");

                while (true)
                {
                    HttpListenerContext context = await listener.GetContextAsync();
                    _ = ProcessRequestAsync(context);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                listener.Close();
            }
        }

        static async Task ProcessRequestAsync(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            Console.WriteLine($"Request received from {request.RemoteEndPoint}");

            // Extract provider name from query string
            string providerName = DefaultProvider;
            if (request.QueryString.Count > 0 && !string.IsNullOrEmpty(request.QueryString.Keys[0]))
            {
                // First segment is the provider name
                providerName = request.QueryString.Keys[0];
            }

            Console.WriteLine($"Using ETW provider: {providerName}");

            // Set headers for streaming response
            response.ContentType = "text/html";
            response.Headers.Add("Cache-Control", "no-cache");
            response.Headers.Add("Connection", "keep-alive");

            using (CancellationTokenSource cancellationTokenSource = new CancellationTokenSource())
            {
                using (TraceEventSession _session = new TraceEventSession("ETWTraceProvider"))
                {
                    try
                    {
                        _session.EnableProvider(providerName);

                        using (StreamWriter writer = new StreamWriter(response.OutputStream, Encoding.UTF8))
                        {
                            // Send initial HTML
                            await writer.WriteAsync("<!DOCTYPE html><html><head><title>ETW Event Stream</title>");
                            await writer.WriteAsync("<style>body{font-family:monospace;margin:20px}h1{color:#333}p{margin:5px 0;padding:5px;border-bottom:1px solid #eee}</style>");
                            await writer.WriteAsync("</head><body>");
                            await writer.WriteAsync($"<h1>ETW Events for Provider: {WebUtility.HtmlEncode(providerName)}</h1>");
                            await writer.FlushAsync();

                            _session.Source.Dynamic.All += async (TraceEvent data) =>
                            {
                                // Format and store the event information
                                string eventInfo = $"Provider: {data.ProviderName}, ID: {data.ID}, Time: {data.TimeStamp}, " +
                                                  $"Process: {data.ProcessID}, Thread: {data.ThreadID}, Event: {data.EventName}";

                                try
                                {
                                    await writer.WriteAsync($"<p>{WebUtility.HtmlEncode(eventInfo)}</p>");
                                    await writer.FlushAsync();
                                }
                                catch (Exception)
                                {
                                    // Handle the case where the client disconnects
                                    cancellationTokenSource.Cancel();
                                }
                            };

                            await Task.Run(() => _session.Source.Process(), cancellationTokenSource.Token);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error with ETW provider '{providerName}': {ex.Message}");
                        response.StatusCode = 400;
                        using (StreamWriter writer = new StreamWriter(response.OutputStream))
                        {
                            await writer.WriteAsync($"<html><body><h1>Error</h1><p>Invalid ETW provider name: {WebUtility.HtmlEncode(providerName)}</p>");
                            await writer.WriteAsync($"<p>Error details: {WebUtility.HtmlEncode(ex.Message)}</p></body></html>");
                        }
                    }
                }
            }
        }
    }
}