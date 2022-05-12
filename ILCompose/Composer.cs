/////////////////////////////////////////////////////////////////////////////////////
//
// ILCompose - Compose partially implementation both .NET language and IL assembler.
// Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)
//
// Licensed under Apache-v2: https://opensource.org/licenses/Apache-2.0
//
/////////////////////////////////////////////////////////////////////////////////////

using System.IO;
using System.Linq;

using Mono.Cecil;

namespace ILCompose
{
    internal sealed class Composer
    {
        private readonly ILogger logger;
        private readonly string basePath;
        private readonly (string from, string to)? adjustCorlib;
        private readonly DefaultAssemblyResolver assemblyResolver = new();

        public Composer(
            ILogger logger, string[] referenceBasePaths,
            (string from, string to)? adjustCorlib)
        {
            this.logger = logger;
            this.basePath = referenceBasePaths[0];
            this.adjustCorlib = adjustCorlib;

            foreach (var referenceBasePath in referenceBasePaths)
            {
                this.assemblyResolver.AddSearchDirectory(referenceBasePath);
                this.logger.Debug($"Reference base path: \"{referenceBasePath}\"");
            }
        }

        private void ComposeMethod(MethodReference forwardrefMethod, MethodReference referenceMethod)
        {

        }

        public bool Compose(string primaryPath, string[] referencePaths)
        {
            this.logger.Information($"Loading primary: \"{primaryPath}\"");
            var primaryAssembly = AssemblyDefinition.ReadAssembly(
                Path.Combine(this.basePath, primaryPath),
                new ReaderParameters
                {
                    AssemblyResolver = this.assemblyResolver,
                    ReadSymbols = true,
                    ReadingMode = ReadingMode.Immediate,
                });

            var referenceModules = referencePaths.
                Select(referencePath =>
                {
                    this.logger.Information($"Loading reference: \"{referencePath}\"");
                    return ModuleDefinition.ReadModule(
                        Path.Combine(this.basePath, referencePath),
                        new ReaderParameters
                        {
                            AssemblyResolver = this.assemblyResolver,
                            ReadSymbols = true,
                            ReadingMode = ReadingMode.Immediate,
                        });
                }).
                ToArray();

            //////////////////////////////////////////////////////////////////////
            // Step 1. Extract forwardref methods from overall primary.

            var forwardrefMethods = primaryAssembly.Modules.
                SelectMany(m => m.Types).
                SelectMany(t => t.Methods).
                Where(m => (m.ImplAttributes & MethodImplAttributes.ForwardRef) == MethodImplAttributes.ForwardRef).
                ToArray();
            this.logger.Trace($"Target forwardrefs: {forwardrefMethods.Length}");

            if (forwardrefMethods.Length == 0)
            {
                this.logger.Information($"Could not any forwardref methods.");
                return true;
            }

            //////////////////////////////////////////////////////////////////////
            // Step 2. Create reference method dictionary.

            var referenceMethods = referenceModules.
                SelectMany(m => m.Types).
                SelectMany(t => t.Methods).
                ToDictionary(m => m.FullName, m => m);
            this.logger.Trace($"Detected reference methods: {referenceMethods.Count}");

            //////////////////////////////////////////////////////////////////////
            // Step 3. Compose

            var composed = 0;
            foreach (var forwardrefMethod in forwardrefMethods)
            {
                var fullName = forwardrefMethod.FullName;
                if (referenceMethods.TryGetValue(fullName, out var referenceMethod))
                {
                    this.ComposeMethod(forwardrefMethod, referenceMethod);
                    this.logger.Trace($"Composed: {fullName}");
                    composed++;
                }
                else
                {
                    this.logger.Error($"Could not find reference method: {fullName}");
                }
            }

            if (composed == forwardrefMethods.Length)
            {
                this.logger.Information($"Composed method total: {composed}");
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
