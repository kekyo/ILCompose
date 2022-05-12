/////////////////////////////////////////////////////////////////////////////////////
//
// ILCompose - Compose partially implementation both .NET language and IL assembler.
// Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)
//
// Licensed under Apache-v2: https://opensource.org/licenses/Apache-2.0
//
/////////////////////////////////////////////////////////////////////////////////////

using System.Collections.Generic;
using System.IO;
using System.Linq;

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace ILCompose
{
    internal sealed class Composer
    {
        private readonly ILogger logger;
        private readonly string basePath;
        private readonly (string from, string to)? adjustCorlib;
        private readonly DefaultAssemblyResolver assemblyResolver = new();
        private readonly Dictionary<Document, Document> cachedDocuments = new();

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

        private void ComposeMethod(
            ModuleDefinition module,
            MethodReference forwardrefMethod,
            MethodReference referenceMethod)
        {
            var fm = forwardrefMethod.Resolve();
            var rm = referenceMethod.Resolve();

            fm.ImplAttributes = rm.ImplAttributes;

            var fbody = fm.Body;
            var rbody = rm.Body;

            //////////////////////////////////////////////////
            
            void CopyCustomAttributes(
                Collection<CustomAttribute> to, Collection<CustomAttribute> from)
            {
                foreach (var fc in from)
                {
                    var tc = new CustomAttribute(module.ImportReference(fc.Constructor));

                    CustomAttributeArgument CloneArgument(CustomAttributeArgument caa) =>
                        new CustomAttributeArgument(
                            module.ImportReference(caa.Type), caa.Value);

                    foreach (var fca in fc.ConstructorArguments)
                    {
                        tc.ConstructorArguments.Add(CloneArgument(fca));
                    }
                    foreach (var ffna in fc.Fields)
                    {
                        tc.Fields.Add(new CustomAttributeNamedArgument(
                            ffna.Name, CloneArgument(ffna.Argument)));
                    }
                    foreach (var fpna in fc.Properties)
                    {
                        tc.Properties.Add(new CustomAttributeNamedArgument(
                            fpna.Name, CloneArgument(fpna.Argument)));
                    }

                    to.Add(tc);
                }
            }

            fbody.InitLocals = rbody.InitLocals;
            fbody.MaxStackSize = rbody.MaxStackSize;

            foreach (var rv in rbody.Variables)
            {
                var fv = new VariableDefinition(module.ImportReference(rv.VariableType));
                fbody.Variables.Add(fv);
            }

            var jumpFixupTargets = new List<(Instruction instruction, int index)>();
            var switchFixupTargets = new List<(Instruction instruction, int[] indices)>();

            foreach (var ri in rbody.Instructions)
            {
                var fi = Instruction.Create(ri.OpCode);
                if (ri.Operand is { } operand)
                {
                    if (operand is FieldReference fr)
                    {
                        fi.Operand = module.ImportReference(fr);
                    }
                    else if (operand is MethodReference mr)
                    {
                        fi.Operand = module.ImportReference(mr);
                    }
                    else if (operand is TypeReference tr)
                    {
                        fi.Operand = module.ImportReference(tr);
                    }
                    else if (operand is VariableReference vr)
                    {
                        fi.Operand = rbody.Variables[vr.Index];
                    }
                    else if (operand is Instruction i)
                    {
                        jumpFixupTargets.Add((fi, i.Offset));
                    }
                    else if (operand is Instruction[] si)
                    {
                        switchFixupTargets.Add((fi, si.Select(i => i.Offset).ToArray()));
                    }
                    else if (operand is CallSite rcs)
                    {
                        var fcs = new CallSite(module.ImportReference(rcs.ReturnType));
                        foreach (var rp in rcs.Parameters)
                        {
                            var fp = new ParameterDefinition(
                                module.ImportReference(rp.ParameterType));
                            fp.Attributes = rp.Attributes;
                            fp.IsReturnValue = rp.IsReturnValue;
                            fp.IsOut = rp.IsOut;
                            fp.IsIn = rp.IsIn;
                            fp.IsOptional = rp.IsOptional;
                            fp.IsLcid = rp.IsLcid;
                            fp.Constant = rp.Constant;
                            CopyCustomAttributes(fp.CustomAttributes, rp.CustomAttributes);
                            fcs.Parameters.Add(fp);
                        }
                        fi.Operand = fcs;
                    }
                    else
                    {
                        fi.Operand = operand;
                    }
                }
                fbody.Instructions.Add(fi);
            }

            foreach (var ft in jumpFixupTargets)
            {
                ft.instruction.Operand = fbody.Instructions[ft.index];
            }
            foreach (var ft in switchFixupTargets)
            {
                ft.instruction.Operand =
                    ft.indices.Select(index => fbody.Instructions[index]).
                    ToArray();
            }

            foreach (var reh in fbody.ExceptionHandlers)
            {
                var feh = new ExceptionHandler(reh.HandlerType);
                feh.TryStart = fbody.Instructions[reh.TryStart.Offset];
                feh.TryEnd = fbody.Instructions[reh.TryEnd.Offset];
                feh.FilterStart = fbody.Instructions[reh.FilterStart.Offset];
                feh.HandlerStart = fbody.Instructions[reh.HandlerStart.Offset];
                feh.HandlerEnd = fbody.Instructions[reh.HandlerEnd.Offset];
                feh.CatchType = module.ImportReference(reh.CatchType);
                rbody.ExceptionHandlers.Add(feh);
            }

            CopyCustomAttributes(fm.CustomAttributes, rm.CustomAttributes);

            foreach (var rsp in rm.DebugInformation.SequencePoints)
            {
                var rd = rsp.Document;
                if (!this.cachedDocuments.TryGetValue(rd, out var fd))
                {
                    fd = new Document(rd.Url);
                    fd.Language = rd.Language;
                    fd.LanguageGuid = rd.LanguageGuid;
                    fd.LanguageVendorGuid = rd.LanguageVendorGuid;
                    fd.LanguageVendor = rd.LanguageVendor;
                    fd.TypeGuid = rd.TypeGuid;
                    fd.EmbeddedSource = rd.EmbeddedSource;
                    this.cachedDocuments.Add(rd, fd);
                }

                var fsp = new SequencePoint(rbody.Instructions[rsp.Offset], fd);
                fsp.StartLine = rsp.StartLine;
                fsp.StartColumn = rsp.StartColumn;
                fsp.EndLine = rsp.EndLine;
                fsp.EndColumn = rsp.EndColumn;

                fm.DebugInformation.SequencePoints.Add(fsp);
            }
        }

        public bool Compose(string primaryPath, string[] referencePaths)
        {
            var primaryDebuggingPath = Path.Combine(
                primaryPath,
                Path.GetFileNameWithoutExtension(primaryPath) + ".pdb");

            // HACK: cecil will lock symbol file when uses defaulted reading method,
            //   (and couldn't replace it manually).
            MemoryStream? symbolStream = null;
            if (File.Exists(primaryDebuggingPath))
            {
                using var pdbStream = new FileStream(
                    primaryDebuggingPath, FileMode.Open, FileAccess.Read, FileShare.None);
                symbolStream = new MemoryStream();
                pdbStream.CopyTo(symbolStream);
                symbolStream.Position = 0;
            }

            this.logger.Information($"Loading primary: \"{primaryPath}\"");

            using var primaryAssembly = AssemblyDefinition.ReadAssembly(
                Path.Combine(this.basePath, primaryPath),
                new ReaderParameters
                {
                    ReadWrite = false,
                    InMemory = true,
                    AssemblyResolver = this.assemblyResolver,
                    ReadSymbols = symbolStream != null,
                    SymbolStream = symbolStream,
                });

            var referenceModules = referencePaths.
                Select(referencePath =>
                {
                    this.logger.Information($"Loading reference: \"{referencePath}\"");
                    return ModuleDefinition.ReadModule(
                        Path.Combine(this.basePath, referencePath),
                        new ReaderParameters
                        {
                            ReadWrite = false,
                            InMemory = true,
                            AssemblyResolver = this.assemblyResolver,
                            ReadSymbols = true,
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
                    this.ComposeMethod(
                        primaryAssembly.MainModule,
                        forwardrefMethod,
                        referenceMethod);
                    this.logger.Trace($"Composed: {fullName}");
                    composed++;
                }
                else
                {
                    this.logger.Error($"Could not find reference method: {fullName}");
                }
            }

            //////////////////////////////////////////////////////////////////////
            // Step 4. Finish

            if (composed == forwardrefMethods.Length)
            {
                var targetBasePath = Path.GetDirectoryName(primaryPath) ?? ".";

                // Backup original assembly and symbol files,
                // because cecil will fail when contains invalid metadata.
                var backupAssemblyPath = Path.Combine(
                    targetBasePath,
                    Path.GetFileNameWithoutExtension(primaryPath) + "_backup" +
                        Path.GetExtension(primaryPath));
                var backupDebuggingPath = Path.Combine(
                    targetBasePath,
                    Path.GetFileNameWithoutExtension(primaryPath) + "_backup.pdb");

                if (File.Exists(backupAssemblyPath))
                {
                    File.Delete(backupAssemblyPath);
                }
                if (File.Exists(backupDebuggingPath))
                {
                    File.Delete(backupDebuggingPath);
                }

                if (File.Exists(primaryPath))
                {
                    File.Move(primaryPath, backupAssemblyPath);
                }
                try
                {
                    if (File.Exists(primaryDebuggingPath))
                    {
                        File.Move(primaryDebuggingPath, backupDebuggingPath);
                    }
                    try
                    {
                        // Write injected assembly and symbol file.
                        primaryAssembly.Write(
                            primaryPath,
                            new WriterParameters
                            {
                                SymbolWriterProvider = new PortablePdbWriterProvider(),
                                WriteSymbols = true,
                                DeterministicMvid = true,
                            });
                    }
                    // Failed:
                    catch
                    {
                        if (File.Exists(primaryDebuggingPath))
                        {
                            File.Delete(primaryDebuggingPath);
                        }
                        if (File.Exists(backupDebuggingPath))
                        {
                            File.Move(backupDebuggingPath, primaryDebuggingPath);
                        }
                        throw;
                    }
                }
                // Failed:
                catch
                {
                    if (File.Exists(primaryPath))
                    {
                        File.Delete(primaryPath);
                    }
                    if (File.Exists(backupAssemblyPath))
                    {
                        File.Move(backupAssemblyPath, primaryPath);
                    }
                    throw;
                }

                // Remove originals.
                if (File.Exists(backupAssemblyPath))
                {
                    File.Delete(backupAssemblyPath);
                }
                if (File.Exists(backupDebuggingPath))
                {
                    File.Delete(backupDebuggingPath);
                }

                this.logger.Information($"Assembly composed: Path={primaryPath}, Methods={composed}");

                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
