
using System.Text.Json.Serialization;
using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.Lambda.SQSEvents;
using WordList.Common.Messaging;
using WordList.Data.Sql;
using WordList.Data.Sql.Models;

public class Function
{
    private readonly static WordDb s_wordDb = new();

    public static async Task<string> FunctionHandler(SQSEvent input, ILambdaContext context)
    {
        var log = new LambdaContextLogger(context);

        log.Info($"Entering UpdateWords FunctionHandler with {input.Records.Count} record(s)");

        var messages = MessageQueues.UpdateWords.Receive(input, log);

        var words = messages.Select(message => new Word
        {
            Text = message.Word,
            Offensiveness = message.Offensiveness,
            Commonness = message.Commonness,
            Sentiment = message.Sentiment,
            WordTypes = message.WordTypes
        }).ToArray();

        log.Info($"Upserting {words.Length} word(s).");

        var result = await s_wordDb.UpsertWordsAsync([.. words]).ConfigureAwait(false);

        log.Info($"Finished upserting {words.Length} words.  Modified: {result.ModifiedWordsCount} word(s), {result.ModifiedWordTypesCount} word type(s), {result.ModifiedWordWordTypesCount} word/type relationship(s)");

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