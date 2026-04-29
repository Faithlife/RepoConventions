using System.Text;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelKeyPressHandler = (_, eventArgs) =>
{
	eventArgs.Cancel = true;
	_ = cancellation.CancelAsync();
};

Console.CancelKeyPress += cancelKeyPressHandler;
try
{
	return await RepoConventions.RepoConventionsCli.InvokeAsync(args, Environment.CurrentDirectory, Console.Out, Console.Error, cancellation.Token);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
	await Console.Error.WriteLineAsync(RepoConventions.RepoConventionsCli.ShutdownRequestedMessage);
	return RepoConventions.RepoConventionsCli.CanceledExitCode;
}
finally
{
	Console.CancelKeyPress -= cancelKeyPressHandler;
}
