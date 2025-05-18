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

        static async Task Main(string[] args)
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add(Url);

            try
            {
                listener.Start();
                Console.WriteLine($"Listening for requests at {Url}");
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

            // Set headers for streaming response
            response.ContentType = "text/html";
            response.Headers.Add("Cache-Control", "no-cache");
            response.Headers.Add("Connection", "keep-alive");

            using (CancellationTokenSource cancellationTokenSource = new CancellationTokenSource())
            {
                using (TraceEventSession _session = new TraceEventSession("ETWTraceProvider"))
                {
                    _session.EnableProvider("Microsoft-Windows-Kernel-Process");

                    using (StreamWriter writer = new StreamWriter(response.OutputStream, Encoding.UTF8))
                    {
                        // Send initial HTML
                        await writer.WriteAsync("<!DOCTYPE html><html><head><title>Streaming Demo</title></head><body>");
                        await writer.FlushAsync();

                        _session.Source.Dynamic.All += async (TraceEvent data) =>
                        {
                            // Format and store the event information
                            string eventInfo = $"Provider: {data.ProviderName}, ID: {data.ID}, Time: {data.TimeStamp}, " +
                                               $"Process: {data.ProcessID}, Thread: {data.ThreadID}, Event: {data.EventName}";

                            try
                            {
                                await writer.WriteAsync($"<p>ETW Event: {eventInfo}</p>");
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
            }
        }
    }
}