using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace DockerWatch
{
    public class Container
    {
        public string ID { get; set; }
        public string Name { get; set; }
        public IList<MountPoint> Mounts { get; set; }
    }

    public class ContainerMonitorEvent
    {
        public string ID { get; set; }
        public string Type { get; set; }
        public Container Container { get; set; }
    }

    public class ContainerEventsMonitor : IDisposable
    {
        private CancellationTokenSource _cancellationTokenSource;

        public ContainerEventsMonitor(CancellationTokenSource cancellationTokenSource)
        {
            _cancellationTokenSource = cancellationTokenSource;
        }

        public Action<ContainerMonitorEvent> OnEvent { get; set; }

        public void Stop()
        {
            OnEvent = null;
            _cancellationTokenSource.Cancel();
        }

        public void Dispose()
        {
            OnEvent = null;
            _cancellationTokenSource.Dispose();
        }
    }

    public class DockerService : IDockerService
    {
        const string DOCKER_URI = "npipe://./pipe/docker_engine";

        private readonly DockerClient _Client;

        public DockerService()
        {
            _Client = new DockerClientConfiguration(new Uri(DOCKER_URI))
                            .CreateClient();
        }

        public async Task<IEnumerable<Container>> GetRunningContainers()
        {
            var running = (
                from container in await GetAllContainers()
                where container.State == "running"
                select new Container()
                {
                    ID = container.ID,
                    Name = container.Image,
                    Mounts = container.Mounts,
                }
            );

            return running;
        }

        private Task<IList<ContainerListResponse>> GetAllContainers()
        {
            return _Client.Containers.ListContainersAsync(
                new ContainersListParameters()
                {
                    All = true
                }
            );
        }

        public async Task<Container> GetContainerById(string containerID)
        {
            var cont = (
                from container in await GetAllContainers()
                where container.ID == containerID
                select new Container()
                {
                    ID = container.ID,
                    Name = container.Image,
                    Mounts = container.Mounts,
                }
            );

            return cont.FirstOrDefault();
        }

        public ContainerEventsMonitor MonitorContainerEvents()
        {
            var tokenSource = new CancellationTokenSource();
            var monitor = new ContainerEventsMonitor(tokenSource);

            var progress = new Progress<JSONMessage>(async message =>
            {
                var container = await GetContainerById(message.ID);
                var e = new ContainerMonitorEvent()
                {
                    ID = message.ID,
                    Type = message.Status,
                    Container = container
                };

                monitor.OnEvent?.Invoke(e);
            });

            var filters = new Dictionary<string, IDictionary<string, bool>>()
            {
                {
                    "type", new Dictionary<string,bool>()
                    {
                        { "container", true }
                    }
                },
                {
                    "event", new Dictionary<string, bool>()
                    {
                        { "start", true },
                        { "die", true},
                    }
                },
            };

            var eventParams = new ContainerEventsParameters()
            {
                Filters = filters,
            };

            _ = _Client.System.MonitorEventsAsync(eventParams, progress, tokenSource.Token);

            return monitor;
        }

        public struct ContainerProcessResult : IDisposable
        {
            public MemoryStream Stdout { get; set; }
            public MemoryStream Stderr { get; set; }
            public short ExitCode { get; set; }

            public void Dispose()
            {
                Stdout?.Dispose();
                Stderr?.Dispose();
            }
        }

        public async Task<ContainerProcessResult> Exec(string containerID, string[] cmd)
        {
            var stdout = new MemoryStream();
            var stderr = new MemoryStream();

            var response = await _Client.Containers.ExecCreateContainerAsync(
                    id: containerID,
                    parameters: new ContainerExecCreateParameters
                    {
                        AttachStderr = true,
                        AttachStdin = false,
                        AttachStdout = true,
                        Cmd = cmd,
                        Detach = false,
                        Tty = false,
                        User = "root",
                        Privileged = true
                    }
            );

            using (var stream = await _Client.Containers.StartAndAttachContainerExecAsync(
                id: response.ID,
                tty: false,
                cancellationToken: default(CancellationToken)
            ))
            {
                await stream.CopyOutputToAsync(null, stdout, stderr, default(CancellationToken));
            }

            stdout.Position = 0;
            stderr.Position = 0;

            return new ContainerProcessResult()
            {
                Stdout = stdout,
                Stderr = stderr,
                ExitCode = (short)(stderr.Length > 0 ? 1 : 0)
            };
        }
    }
}
