using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipes;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace InterProcessCommunicationPipes
{
    public class InterProcessCommunication
    {
        public interface INamedPipeCommunicator
        {
            void StartNotify();
            void StopNotify();
        }


        private const string PipeName = "my_pipe_name";



        /// <summary>
        /// Server Class
        /// </summary>
        public class NamedPipeServer : INamedPipeCommunicator
        {
            private ConcurrentBag<NamedPipeServerStream> _clients = new ConcurrentBag<NamedPipeServerStream>();
            private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
            private readonly object _clientsLock = new object();
            public async Task SendMessageAsync(string message)
            {
                List<Task> tasks = new List<Task>();
                List<NamedPipeServerStream> disconnectedClients = new List<NamedPipeServerStream>();

                lock (_clientsLock)
                {
                    foreach (var client in _clients)
                    {
                        if (client.IsConnected)
                        {
                            tasks.Add(Task.Run(async () =>
                            {
                                try
                                {
                                    using (var writer = new StreamWriter(client) { AutoFlush = true })
                                    {
                                        await writer.WriteLineAsync(message);
                                    }
                                }
                                catch (IOException)
                                {
                                    lock (_clientsLock)
                                    {
                                        disconnectedClients.Add(client);
                                    }
                                }
                            }));
                        }
                        else
                        {
                            disconnectedClients.Add(client);
                        }
                    }

                    foreach (var client in disconnectedClients)
                    {
                        _clients = new ConcurrentBag<NamedPipeServerStream>(_clients.Except(new[] { client }));
                    }
                }

                await Task.WhenAll(tasks);
            }
      
            public void StartNotify()
            {
                Task.Run(() => ListenForClients(_cancellationTokenSource.Token));
            }

            public void StopNotify()
            {
                _cancellationTokenSource.Cancel();
            }

            private async Task ListenForClients(CancellationToken cancellationToken)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.Out, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    try
                    {
                        await pipeServer.WaitForConnectionAsync(cancellationToken);
                        lock (_clientsLock)
                        {
                            _clients.Add(pipeServer);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        pipeServer.Dispose();
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error accepting client connection: {ex.Message}");
                        pipeServer.Dispose();
                    }
                }
            }
        }






        /// <summary>
        /// Client Class
        /// </summary>
        public class NamedPipeClient : INamedPipeCommunicator
        {
            private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
            private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(300);
            private readonly ConcurrentDictionary<string, DateTime> _debounceDict = new ConcurrentDictionary<string, DateTime>();

            public void StartNotify()
            {
                Task.Run(() => ListenForMessages(_cancellationTokenSource.Token));
            }

            public void StopNotify()
            {
                _cancellationTokenSource.Cancel();
            }

            private async Task ListenForMessages(CancellationToken cancellationToken)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        using (var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.In, PipeOptions.Asynchronous))
                        {
                            await pipeClient.ConnectAsync(cancellationToken);

                            using (var reader = new StreamReader(pipeClient))
                            {
                                while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                                {
                                    string message = await reader.ReadLineAsync();
                                    if (message != null)
                                    {
                                        HandleDebouncedEvent(message);
                                    }
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error listening for messages: {ex.Message}");
                        await Task.Delay(1000, cancellationToken);
                    }
                }
            }

            static string guidTemp = Guid.NewGuid().ToString();

            bool _isFirst = true;
            private void HandleDebouncedEvent(string message)
            {
                var now = DateTime.Now;
                var lastExecutionTime = _debounceDict.GetOrAdd(message, now);

                if ((now - lastExecutionTime) >= _debounceInterval || _isFirst)
                {
                    _isFirst = false;
                    _debounceDict[message] = now;
                    HandleCustomEvent(message);
                }
            }
            private void HandleCustomEvent(string message)
            {
                try
                {
                    Console.WriteLine($"Message: {message}");
                }
                catch
                {}
            }
        }


    }
}
