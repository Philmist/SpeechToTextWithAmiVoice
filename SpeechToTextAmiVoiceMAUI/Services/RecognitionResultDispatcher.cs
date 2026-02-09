using SpeechToText.Core;

namespace SpeechToTextAmiVoiceMAUI.Services;

public sealed class RecognitionResultDispatcher
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly object sync = new();

    private TextHttpSender? textHttpSender;
    private BouyomiChanSender? bouyomiChanSender;
    private bool enableHttpPost;
    private bool enableBouyomi;
    private string bouyomiPrefix = "";

    public RecognitionResultDispatcher(IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;
    }

    public void Configure(ConnectionSettings connectionSettings, RuntimeOptions runtimeOptions)
    {
        lock (sync)
        {
            enableHttpPost = runtimeOptions.EnableHttpPost;
            enableBouyomi = runtimeOptions.EnableBouyomi;
            bouyomiPrefix = connectionSettings.BouyomiPrefix?.Trim() ?? "";

            textHttpSender = enableHttpPost
                ? new TextHttpSender(connectionSettings.HttpPostUri?.Trim() ?? "", httpClientFactory)
                : null;

            bouyomiChanSender = enableBouyomi
                ? new BouyomiChanSender(
                    connectionSettings.BouyomiHost?.Trim() ?? "127.0.0.1",
                    connectionSettings.BouyomiPort,
                    connectionSettings.BouyomiVoiceTone)
                : null;
        }
    }

    public Task DispatchAsync(string recognizedText)
    {
        if (string.IsNullOrWhiteSpace(recognizedText))
        {
            return Task.CompletedTask;
        }

        TextHttpSender? currentTextHttpSender;
        BouyomiChanSender? currentBouyomiChanSender;
        bool currentEnableHttpPost;
        bool currentEnableBouyomi;
        string currentBouyomiPrefix;

        lock (sync)
        {
            currentTextHttpSender = textHttpSender;
            currentBouyomiChanSender = bouyomiChanSender;
            currentEnableHttpPost = enableHttpPost;
            currentEnableBouyomi = enableBouyomi;
            currentBouyomiPrefix = bouyomiPrefix;
        }

        List<Task>? tasks = null;

        if (currentEnableBouyomi && currentBouyomiChanSender is not null)
        {
            tasks ??= new List<Task>();
            var sendText = (currentBouyomiPrefix + recognizedText).Trim();
            tasks.Add(Task.Run(() => currentBouyomiChanSender.Send(sendText)));
        }

        if (currentEnableHttpPost && currentTextHttpSender is not null)
        {
            tasks ??= new List<Task>();
            tasks.Add(currentTextHttpSender.Send(new TextHttpSender.RecognizedText { text = recognizedText }));
        }

        if (tasks is null)
        {
            return Task.CompletedTask;
        }

        return Task.WhenAll(tasks);
    }
}
