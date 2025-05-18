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
        private static EtwManifestUserDataReader etwManifestUserDataReader = new EtwManifestUserDataReader();
        private static readonly string Url = "http://localhost:5000/";
        private const string DefaultProvider = "30336ED4-E327-447C-9DE0-51B652C86108";

        static async Task Main(string[] args)
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add(Url);

            try
            {
                listener.Start();
                Console.WriteLine($"Listening for requests at {Url}");
                Console.WriteLine("Usage: http://localhost:5000/?[ETWProviderName]");
                Console.WriteLine($"Example: http://localhost:5000/?{DefaultProvider}");
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
            string providerInput = DefaultProvider;
            if (request.QueryString.Count > 0 && !string.IsNullOrEmpty(request.QueryString.Keys[0]))
            {
                // First segment is the provider name
                providerInput = request.QueryString.Keys[0];
            }

            // User can pass in the GUID or provider Name.
            EtwUserDataSchema etwUserDataSchema;
            if (Guid.TryParse(providerInput, out Guid providerGuid))
            {
                etwUserDataSchema = etwManifestUserDataReader.GetUserDataSchema(providerGuid);
            }
            else
            {
                Console.WriteLine($"Error: Invalid provider guid '{providerInput}'");
                return;
            }

            Console.WriteLine($"Using ETW provider: {providerInput}");

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
                        _session.EnableProvider(providerInput);

                        using (StreamWriter writer = new StreamWriter(response.OutputStream, Encoding.UTF8))
                        {
                            // Send initial HTML
                            await writer.WriteAsync("<!DOCTYPE html><html><head><title>ETW Event Stream</title>");
                            await writer.WriteAsync("<style>body{font-family:monospace;margin:20px}h1{color:#333}p{margin:5px 0;padding:5px;border-bottom:1px solid #eee}</style>");
                            await writer.WriteAsync("</head><body>");
                            await writer.WriteAsync($"<h1>ETW Events for Provider: {WebUtility.HtmlEncode(providerInput)}</h1>");
                            await writer.FlushAsync();

                            _session.Source.Dynamic.All += async (TraceEvent traceEvent) =>
                            {
                                var userData = etwManifestUserDataReader.ParseTraceEventUserData(etwUserDataSchema, traceEvent);

                                // Format and store the event information
                                string eventInfo =
                                    $"Provider: {traceEvent.ProviderName}, ID: {traceEvent.ID}, Time: {traceEvent.TimeStamp}, " +
                                    $"Process: {traceEvent.ProcessID}, Thread: {traceEvent.ThreadID}, Event: {traceEvent.EventName}";
                                    
                                foreach(var key in userData.Keys)
                                {
                                    eventInfo += $", {key}: {userData[key]}";
                                }

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
                        Console.WriteLine($"Error with ETW provider '{providerInput}': {ex.Message}");
                        response.StatusCode = 400;
                        using (StreamWriter writer = new StreamWriter(response.OutputStream))
                        {
                            await writer.WriteAsync($"<html><body><h1>Error</h1><p>Invalid ETW provider name: {WebUtility.HtmlEncode(providerInput)}</p>");
                            await writer.WriteAsync($"<p>Error details: {WebUtility.HtmlEncode(ex.Message)}</p></body></html>");
                        }
                    }
                }
            }
        }
    }
}