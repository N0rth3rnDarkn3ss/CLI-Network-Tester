using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=============================================");
Console.WriteLine("             CLI Network Tester              ");
Console.WriteLine("=============================================");

// User test duration input
Console.Write("Enter test duration in seconds (Default is 5s): ");
string? input = Console.ReadLine();

// Check for valid number, otherwise default
if (!int.TryParse(input, out int testDurationSeconds) || testDurationSeconds <= 0)
{
    testDurationSeconds = 5;
}

Console.WriteLine($"\nRunning sustained accuracy tests ({testDurationSeconds}s per block)... \n");

// Ping run
long pingTime = await RunTestWithSpinnerAsync("Checking Ping...", () => RunPingTestAsync("1.1.1.1"));

// Sustained Download run
string downloadUrl = "https://cloudflare.com"; // 5MB chunks
double downloadMbps = await RunTestWithSpinnerAsync("Testing Download Speed...", () => RunSustainedDownloadAsync(downloadUrl, testDurationSeconds));

// Sustained Upload run
string uploadUrl = "https://cloudflare.com";
double uploadMbps = await RunTestWithSpinnerAsync("Testing Upload Speed...", () => RunSustainedUploadAsync(uploadUrl, testDurationSeconds));

// Results output
Console.WriteLine("\n=============================================");
Console.WriteLine("                TEST RESULTS                 ");
Console.WriteLine("=============================================");
Console.WriteLine($" Duration set:  {testDurationSeconds} seconds per test");
Console.WriteLine($" Ping:          {(pingTime >= 0 ? $"{pingTime} ms" : "N/A")}");
Console.WriteLine($" Download:      {downloadMbps:F2} Mbps (Sustained)");
Console.WriteLine($" Upload:        {uploadMbps:F2} Mbps (Sustained)");
Console.WriteLine("=============================================");

Console.WriteLine("\nPress any key to exit...");
Console.ReadKey();


// Loading spinner logic & wrapper

async Task<T> RunTestWithSpinnerAsync<T>(string message, Func<Task<T>> networkTask)
{
    Console.Write($"{message}  ");

    int spinnerLeft = Console.CursorLeft - 1;
    int spinnerTop = Console.CursorTop;

    using CancellationTokenSource cts = new CancellationTokenSource();
    Task spinnerTask = Task.Run(() => AnimateSpinner(spinnerLeft, spinnerTop, cts.Token));

    try
    {
        return await networkTask();
    }
    finally
    {
        cts.Cancel();
        await Task.WhenAny(spinnerTask);

        Console.SetCursorPosition(spinnerLeft, spinnerTop);
        Console.WriteLine("Done. ");
    }
}

async Task AnimateSpinner(int left, int top, CancellationToken token)
{
    string[] spinnerFrames = { "/", "-", "\\", "|" };
    int currentFrame = 0;

    while (!token.IsCancellationRequested)
    {
        try
        {
            Console.SetCursorPosition(left, top);
            Console.Write(spinnerFrames[currentFrame]);
            currentFrame = (currentFrame + 1) % spinnerFrames.Length;
            await Task.Delay(100, token);
        }
        catch (TaskCanceledException)
        {
            break;
        }
    }
}


// Sustained test implementations

async Task<long> RunPingTestAsync(string host)
{
    try
    {
        using Ping pingSender = new Ping();
        PingReply reply = await pingSender.SendPingAsync(host, 3000);
        if (reply.Status == IPStatus.Success) return reply.RoundtripTime;
    }
    catch { }
    return -1;
}

async Task<double> RunSustainedDownloadAsync(string url, int durationSeconds)
{
    try
    {
        using HttpClient client = new HttpClient();
        long totalBytesDownloaded = 0;

        Stopwatch stopwatch = Stopwatch.StartNew();
        TimeSpan timeLimit = TimeSpan.FromSeconds(durationSeconds);

        while (stopwatch.Elapsed < timeLimit)
        {
            using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) continue;

            using Stream stream = await response.Content.ReadAsStreamAsync();
            byte[] buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                totalBytesDownloaded += bytesRead;
                if (stopwatch.Elapsed >= timeLimit)
                    break;
            }
        }
        stopwatch.Stop();

        double totalSeconds = stopwatch.Elapsed.TotalSeconds;
        double totalMegabits = (totalBytesDownloaded * 8) / 1_000_000.0;

        return totalMegabits / totalSeconds;
    }
    catch
    {
        return 0.0;
    }
}

async Task<double> RunSustainedUploadAsync(string url, int durationSeconds)
{
    try
    {
        using HttpClient client = new HttpClient();
        long totalBytesUploaded = 0;

        byte[] chunkData = new byte[2 * 1024 * 1024];
        new Random().NextBytes(chunkData);

        Stopwatch stopwatch = Stopwatch.StartNew();
        TimeSpan timeLimit = TimeSpan.FromSeconds(durationSeconds);

        while (stopwatch.Elapsed < timeLimit)
        {
            using ByteArrayContent content = new ByteArrayContent(chunkData);
            HttpResponseMessage response = await client.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                totalBytesUploaded += chunkData.Length;
            }
        }
        stopwatch.Stop();

        double totalSeconds = stopwatch.Elapsed.TotalSeconds;
        double totalMegabits = (totalBytesUploaded * 8) / 1_000_000.0;

        return totalMegabits / totalSeconds;
    }
    catch
    {
        return 0.0;
    }
}
