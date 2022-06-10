/////////////////////////////////////////////////////////////////////////////////////
//
// ILCompose - Compose partially implementation both .NET language and IL assembler.
// Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)
//
// Licensed under Apache-v2: https://opensource.org/licenses/Apache-2.0
//
/////////////////////////////////////////////////////////////////////////////////////

using System.IO;

using Mono.Cecil;

namespace ILCompose
{
    internal sealed class AssemblyResolver : DefaultAssemblyResolver
    {
        private readonly ILogger logger;

        public AssemblyResolver(ILogger logger, string[] referenceBasePaths)
        {
            this.logger = logger;
            foreach (var referenceBasePath in referenceBasePaths)
            {
                var fullPath = Path.GetFullPath(referenceBasePath);
                base.AddSearchDirectory(fullPath);
                this.logger.Trace($"Reference base path: {fullPath}");
            }
        }

        public override AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            var parameters = new ReaderParameters()
            {
                ReadWrite = false,
                InMemory = true,
                AssemblyResolver = this,
                SymbolReaderProvider = new SymbolReaderProvider(this.logger),
                ReadSymbols = true,
            };
            var ad = base.Resolve(name, parameters);
            this.logger.Trace($"Assembly loaded: {ad.MainModule.FileName}");
            return ad;
        }

        public AssemblyDefinition ReadAssemblyFrom(string assemblyPath)
        {
            var parameters = new ReaderParameters()
            {
                ReadWrite = false,
                InMemory = true,
                AssemblyResolver = this,
                SymbolReaderProvider = new SymbolReaderProvider(this.logger),
                ReadSymbols = true,
            };
            var ad = AssemblyDefinition.ReadAssembly(assemblyPath, parameters);
            this.logger.Trace($"Assembly loaded: {ad.MainModule.FileName}");
            return ad;
        }

        public ModuleDefinition ReadModuleFrom(string modulePath)
        {
            var parameters = new ReaderParameters()
            {
                ReadWrite = false,
                InMemory = true,
                AssemblyResolver = this,
                SymbolReaderProvider = new SymbolReaderProvider(this.logger),
                ReadSymbols = true,
            };
            var md = ModuleDefinition.ReadModule(modulePath, parameters);
            this.logger.Trace($"Module loaded: {md.FileName}");
            return md;
        }
    }
}
