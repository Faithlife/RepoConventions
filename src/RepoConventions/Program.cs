using System.Text;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

return await RepoConventions.RepoConventionsCli.InvokeAsync(args, Environment.CurrentDirectory, Console.Out, Console.Error, CancellationToken.None);
