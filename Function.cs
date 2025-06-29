
using System.Data;
using System.Text.Json.Serialization;
using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.Lambda.SQSEvents;
using WordList.Common.Messaging;
using WordList.Common.Status;
using WordList.Common.Status.Models;
using WordList.Common.Words;
using WordList.Common.Words.Models;

public class Function
{
    private readonly static WordDb s_wordDb = new();

    public static async Task<string> FunctionHandler(SQSEvent input, ILambdaContext context)
    {
        var log = new LambdaContextLogger(context);

        log.Info($"Entering UpdateWords FunctionHandler with {input.Records.Count} record(s)");

        var messages = MessageQueues.UpdateWords.Receive(input, log).GroupBy(message => message.CorrelationId);

        var validAttributeList = await WordAttributes.GetAllAsync().ConfigureAwait(false);
        var validAttributeNames = validAttributeList.Select(a => a.Name).ToHashSet();

        foreach (var group in messages)
        {
            var status = new StatusClient(group.Key);
            await status.UpdateStatusAsync(SourceStatus.UPDATING).ConfigureAwait(false);

            var words = group.Select(message => new Word
            {
                Text = message.Word,
                Attributes = message.Attributes.Where(attr => validAttributeNames.Contains(attr.Key)).ToDictionary()
            }).ToArray();

            log.Info($"Upserting {words.Length} word(s) for correlation ID {group.Key}.");

            var result = await s_wordDb.UpsertWordsAsync(words).ConfigureAwait(false);
            await status.IncreaseProcessedWordsAsync(result.ModifiedWordsCount).ConfigureAwait(false);

            log.Info($"Finished upserting {words.Length} words for correlation ID |{group.Key}.  Modified: {result.ModifiedWordsCount} word(s), {result.ModifiedWordTypesCount} word type(s), {result.ModifiedWordWordTypesCount} word/type relationship(s)");
        }

        return "ok";
    }

    public static async Task Main()
    {
        Func<SQSEvent, ILambdaContext, Task<string>> handler = FunctionHandler;
        await LambdaBootstrapBuilder.Create(handler, new SourceGeneratorLambdaJsonSerializer<LambdaFunctionJsonSerializerContext>())
            .Build()
            .RunAsync();
    }
}

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(SQSEvent))]
public partial class LambdaFunctionJsonSerializerContext : JsonSerializerContext
{
}