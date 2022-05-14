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
using Mono.Cecil.Pdb;
using Mono.Collections.Generic;

namespace ILCompose
{
    internal sealed class Composer
    {
        private readonly ILogger logger;
        private readonly string basePath;
        private readonly bool adjustAssemblyReferences;
        private readonly DefaultAssemblyResolver assemblyResolver = new();
        private readonly Dictionary<Document, Document> cachedDocuments = new();

        public Composer(
            ILogger logger, string[] referenceBasePaths, bool adjustAssemblyReferences)
        {
            this.logger = logger;
            this.basePath = referenceBasePaths[0];
            this.adjustAssemblyReferences = adjustAssemblyReferences;

            foreach (var referenceBasePath in referenceBasePaths)
            {
                this.assemblyResolver.AddSearchDirectory(referenceBasePath);
                this.logger.Debug($"Reference base path: \"{referenceBasePath}\"");
            }
        }

        private void ComposeMethod(
            MethodReference forwardrefMethod,
            MethodReference referenceMethod,
            ReferenceImporter importer)
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
                    var tc = new CustomAttribute(importer.Import(fc.Constructor));

                    CustomAttributeArgument CloneArgument(CustomAttributeArgument caa) =>
                        new CustomAttributeArgument(
                            importer.Import(caa.Type), caa.Value);

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
                var fv = new VariableDefinition(importer.Import(rv.VariableType));
                fbody.Variables.Add(fv);
            }

            var jumpFixupTargets = new List<(Instruction instruction, int index)>();
            var switchFixupTargets = new List<(Instruction instruction, int[] indices)>();
            var lookupIndexFromOffset = rbody.Instructions.
                Select((ri, index) => (ri, index)).
                ToDictionary(entry => entry.ri.Offset, entry => entry.index);

            var dummyInstruction = Instruction.Create(OpCodes.Nop);

            Instruction ReserveForJumpFixup(Instruction ri, Instruction i)
            {
                var ni = Instruction.Create(ri.OpCode, dummyInstruction);
                jumpFixupTargets!.Add((ni, lookupIndexFromOffset[i.Offset]));
                return ni;
            }
            Instruction ReserveForSwitchFixup(Instruction ri, Instruction[] si)
            {
                var ni = Instruction.Create(ri.OpCode, dummyInstruction);
                switchFixupTargets!.Add((ni, si.Select(i => lookupIndexFromOffset[i.Offset]).ToArray()));
                return ni;
            }
            Instruction CloneCallSite(Instruction ri, CallSite rcs)
            {
                var fcs = new CallSite(importer.Import(rcs.ReturnType));
                foreach (var rp in rcs.Parameters)
                {
                    var fp = new ParameterDefinition(
                        importer.Import(rp.ParameterType));
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
                return Instruction.Create(ri.OpCode, fcs);
            }

            foreach (var ri in rbody.Instructions)
            {
                var fi = ri.Operand switch
                {
                    FieldReference fr => Instruction.Create(ri.OpCode, importer.Import(fr)),
                    MethodReference mr => Instruction.Create(ri.OpCode, importer.Import(mr)),
                    TypeReference tr => Instruction.Create(ri.OpCode, importer.Import(tr)),
                    VariableReference vr => Instruction.Create(ri.OpCode, rbody.Variables[vr.Index]),
                    ParameterReference pr => Instruction.Create(ri.OpCode, rbody.Method.Parameters[pr.Index]),
                    Instruction i => ReserveForJumpFixup(ri, i),
                    Instruction[] si => ReserveForSwitchFixup(ri, si),
                    CallSite rcs => CloneCallSite(ri, rcs),
                    byte value => Instruction.Create(ri.OpCode, value),
                    sbyte value => Instruction.Create(ri.OpCode, value),
                    int value => Instruction.Create(ri.OpCode, value),
                    long value => Instruction.Create(ri.OpCode, value),
                    float value => Instruction.Create(ri.OpCode, value),
                    double value => Instruction.Create(ri.OpCode, value),
                    string value => Instruction.Create(ri.OpCode, value),
                    _ => Instruction.Create(ri.OpCode),
                };
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
                feh.CatchType = importer.Import(reh.CatchType);
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

                var fsp = new SequencePoint(
                    rbody.Instructions[lookupIndexFromOffset[rsp.Offset]], fd);
                fsp.StartLine = rsp.StartLine;
                fsp.StartColumn = rsp.StartColumn;
                fsp.EndLine = rsp.EndLine;
                fsp.EndColumn = rsp.EndColumn;

                fm.DebugInformation.SequencePoints.Add(fsp);
            }
        }

        public bool Compose(string primaryAssemblyPath, string[] referenceAssemblyPaths)
        {
            var primaryDebuggingPath = Path.Combine(
                primaryAssemblyPath,
                Path.GetFileNameWithoutExtension(primaryAssemblyPath) + ".pdb");

            // HACK: cecil will lock symbol file when uses defaulted reading method,
            //   (and couldn't replace it manually).
            MemoryStream? symbolStream = null;
            if (File.Exists(primaryDebuggingPath))
            {
                this.logger.Trace($"Loading primary pdb: \"{primaryDebuggingPath}\"");

                using var pdbStream = new FileStream(
                    primaryDebuggingPath, FileMode.Open, FileAccess.Read, FileShare.None);
                symbolStream = new MemoryStream();
                pdbStream.CopyTo(symbolStream);
                symbolStream.Position = 0;
            }

            this.logger.Information($"Loading primary: \"{primaryAssemblyPath}\"");

            using var primaryAssembly = AssemblyDefinition.ReadAssembly(
                Path.Combine(this.basePath, primaryAssemblyPath),
                new ReaderParameters
                {
                    ReadWrite = false,
                    InMemory = true,
                    AssemblyResolver = this.assemblyResolver,
                    SymbolReaderProvider = new PdbReaderProvider(),
                    ReadSymbols = true,
                    SymbolStream = symbolStream,
                });

            var referenceModules = referenceAssemblyPaths.
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
                            SymbolReaderProvider = new PdbReaderProvider(),
                            ReadSymbols = true,
                        });
                }).
                ToArray();

            //////////////////////////////////////////////////////////////////////
            // Step 1. Extract forwardref methods from overall primary.

            var primaryMethods = primaryAssembly.Modules.
                SelectMany(m => m.Types).
                SelectMany(t => t.Methods).
                Where(m => (m.ImplAttributes & MethodImplAttributes.ForwardRef) == MethodImplAttributes.ForwardRef).
                ToArray();
            this.logger.Trace($"Target forwardrefs: {primaryMethods.Length}");

            if (primaryMethods.Length == 0)
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

            var importer = new ReferenceImporter(primaryAssembly.MainModule);
            if (this.adjustAssemblyReferences)
            {
                foreach (var type in primaryAssembly.Modules.
                    SelectMany(m => m.AssemblyReferences).
                    SelectMany(anr => this.assemblyResolver.Resolve(anr).Modules).
                    SelectMany(m => m.Types).
                    Where(t => (t.IsPublic || t.IsNestedPublic) && t.BaseType != null))
                {
                    importer.RegisterForward(type);
                }
            }

            var composed = 0;
            foreach (var forwardrefMethod in primaryMethods)
            {
                var fullName = forwardrefMethod.FullName;
                if (referenceMethods.TryGetValue(fullName, out var referenceMethod))
                {
                    this.ComposeMethod(
                        forwardrefMethod,
                        referenceMethod,
                        importer);
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

            if (composed == primaryMethods.Length)
            {
                var targetBasePath = Path.GetDirectoryName(primaryAssemblyPath) ?? ".";

                // Backup original assembly and symbol files,
                // because cecil will fail when contains invalid metadata.
                var backupAssemblyPath = Path.Combine(
                    targetBasePath,
                    Path.GetFileNameWithoutExtension(primaryAssemblyPath) + "_backup" +
                        Path.GetExtension(primaryAssemblyPath));
                var backupDebuggingPath = Path.Combine(
                    targetBasePath,
                    Path.GetFileNameWithoutExtension(primaryAssemblyPath) + "_backup.pdb");

                if (File.Exists(backupAssemblyPath))
                {
                    File.Delete(backupAssemblyPath);
                }
                if (File.Exists(backupDebuggingPath))
                {
                    File.Delete(backupDebuggingPath);
                }

                if (File.Exists(primaryAssemblyPath))
                {
                    File.Move(primaryAssemblyPath, backupAssemblyPath);
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
                            primaryAssemblyPath,
                            new WriterParameters
                            {
                                SymbolWriterProvider = new PdbWriterProvider(),
                                WriteSymbols = true,
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
                    if (File.Exists(primaryAssemblyPath))
                    {
                        File.Delete(primaryAssemblyPath);
                    }
                    if (File.Exists(backupAssemblyPath))
                    {
                        File.Move(backupAssemblyPath, primaryAssemblyPath);
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

                this.logger.Information($"Assembly composed: Path={primaryAssemblyPath}, Methods={composed}");

                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
