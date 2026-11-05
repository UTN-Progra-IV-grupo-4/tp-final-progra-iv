extern alias paymentgrpc;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using PaymentGrpcService = paymentgrpc::Payment.Grpc.Services.PaymentGrpcService;

namespace Booking.Tests.Payments;

// Runs Payment.Grpc as a separate process with authentication enabled, as it runs in an environment.
internal sealed class PaymentProcess : IAsyncDisposable
{
    private readonly Process process;
    public string Address { get; }
    public ConcurrentQueue<string> LogLines { get; } = new();

    private PaymentProcess(Process process, string address)
    {
        this.process = process;
        Address = address;
        process.OutputDataReceived += (_, eventArgs) => AddLog(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => AddLog(eventArgs.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    public static async Task<PaymentProcess> StartAsync(string connectionString, string authority)
    {
        var portPicker = new TcpListener(IPAddress.Loopback, 0);
        portPicker.Start();
        var port = ((IPEndPoint)portPicker.LocalEndpoint).Port;
        portPicker.Stop();
        var address = $"http://127.0.0.1:{port}";

        var assemblyPath = typeof(PaymentGrpcService).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = FindPaymentProjectDirectory()
        };
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["ConnectionStrings__Payment"] = connectionString;
        startInfo.Environment["SimulatedGateway__Mode"] = "Approve";
        startInfo.Environment["Charge__SupportedCurrencies__0"] = "ARS";
        startInfo.Environment["Charge__PendingTimeoutSeconds"] = "60";
        startInfo.Environment["Kestrel__Endpoints__Grpc__Url"] = address;
        startInfo.Environment["Kestrel__Endpoints__Grpc__Protocols"] = "Http2";
        // Payment only accepts tokens from this authority; the tests run a local stand-in for Keycloak.
        startInfo.Environment["Authentication__Authority"] = authority;
        startInfo.Environment["Authentication__RequireHttpsMetadata"] = "false";

        var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start local Payment process.");
        var instance = new PaymentProcess(process, address);
        try
        {
            var until = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < until)
            {
                if (process.HasExited)
                    throw new InvalidOperationException("Local Payment process exited before listening.");

                try
                {
                    using var socket = new TcpClient();
                    await socket.ConnectAsync(IPAddress.Loopback, port);
                    return instance;
                }
                catch (SocketException)
                {
                    await Task.Delay(100);
                }
            }

            throw new TimeoutException("Local Payment process did not listen within 15 seconds.");
        }
        catch
        {
            await instance.DisposeAsync();
            throw;
        }
    }

    private void AddLog(string? line)
    {
        if (line is not null)
            LogLines.Enqueue(line);
    }

    public async Task WaitForLogAsync(Func<string, bool> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(3);
        while (!LogLines.Any(predicate) && DateTime.UtcNow < until)
            await Task.Delay(25);
    }

    private static string FindPaymentProjectDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var projectDirectory = Path.Combine(directory.FullName, "src", "Payment", "Payment.Grpc");
            if (File.Exists(Path.Combine(projectDirectory, "Payment.Grpc.csproj")))
                return projectDirectory;
        }

        throw new DirectoryNotFoundException("Payment.Grpc project directory was not found.");
    }

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        process.Dispose();
    }
}
