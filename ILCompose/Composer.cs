/////////////////////////////////////////////////////////////////////////////////////
//
// ILCompose - Compose partially implementation both .NET language and IL assembler.
// Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)
//
// Licensed under Apache-v2: https://opensource.org/licenses/Apache-2.0
//
/////////////////////////////////////////////////////////////////////////////////////

using System;
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
        private readonly AssemblyResolver assemblyResolver;
        private readonly Dictionary<Document, Document> cachedDocuments = new();

        public Composer(
            ILogger logger, string[] referenceBasePaths, bool adjustAssemblyReferences)
        {
            this.logger = logger;
            this.basePath = referenceBasePaths[0];
            this.adjustAssemblyReferences = adjustAssemblyReferences;
            this.assemblyResolver = new(this.logger, referenceBasePaths);
        }

        private void ComposeMethod(
            MethodDefinition forwardrefMethod,
            MethodDefinition referenceMethod,
            ReferenceImporter importer,
            MethodReference methodImplConstructor)
        {
            var fbody = new MethodBody(forwardrefMethod);
            var rbody = referenceMethod.Body;

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

            // Updated new body.
            forwardrefMethod.Body = fbody;

            //////////////////////////////////////////////////

            // Drop forwardref
            forwardrefMethod.ImplAttributes = referenceMethod.ImplAttributes & ~MethodImplAttributes.ForwardRef;

            CopyCustomAttributes(
                forwardrefMethod.CustomAttributes,
                referenceMethod.CustomAttributes);

            if (!Utilities.IsForwardRef(forwardrefMethod))
            {
                // Add `MethodImplAttribute`
                // HACK: See `IsForwardRef()`
                var c = importer.Import(methodImplConstructor);
                var mio = importer.Import(c.Parameters[0].ParameterType);
                var tc = new CustomAttribute(c);
                var tca = new CustomAttributeArgument(
                    mio,
                    mio.FullName == "System.Runtime.CompilerServices.MethodImplOptions" ?
                        MethodImplAttributes.ForwardRef :
                        (short)MethodImplAttributes.ForwardRef);
                tc.ConstructorArguments.Add(tca);

                forwardrefMethod.CustomAttributes.Add(tc);
            }

            //////////////////////////////////////////////////

            foreach (var rsp in referenceMethod.DebugInformation.SequencePoints)
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

                forwardrefMethod.DebugInformation.SequencePoints.Add(fsp);
            }
        }

        private sealed class ModuleDefinitionComparer : IEqualityComparer<ModuleDefinition>
        {
            public bool Equals(ModuleDefinition? x, ModuleDefinition? y) =>
                x!.FileName.Equals(y!.FileName);

            public int GetHashCode(ModuleDefinition obj) =>
                obj.FileName.GetHashCode();
        }

        public bool Compose(string primaryAssemblyPath, string[] ilModulePaths)
        {
            this.logger.Information($"Loading primary: {primaryAssemblyPath}");

            using var primaryAssembly = this.assemblyResolver.ReadAssemblyFrom(
                Path.Combine(this.basePath, primaryAssemblyPath));

            var ilModules = ilModulePaths.
                Select(ilModulePath =>
                {
                    this.logger.Information($"Loading IL module: {ilModulePath}");
                    return this.assemblyResolver.ReadModuleFrom(
                        Path.Combine(this.basePath, ilModulePath));
                }).
                ToArray();

            //////////////////////////////////////////////////////////////////////
            // Step 1. Extract forwardref methods from overall primary.

            var primaryMethods = primaryAssembly.Modules.
                SelectMany(m => m.Types).
                SelectMany(t => t.Methods).
                Where(Utilities.IsForwardRef).
                ToArray();
            this.logger.Trace($"Target forwardrefs: {primaryMethods.Length}");

            if (primaryMethods.Length == 0)
            {
                this.logger.Warning($"Could not any forwardref methods.");
                return true;
            }

            //////////////////////////////////////////////////////////////////////
            // Step 2. Create IL method dictionary.

            var ilMethods = ilModules.
                SelectMany(m => m.Types).
                SelectMany(t => t.Methods).
                ToDictionary(m => m.FullName, m => m);
            this.logger.Trace($"Detected IL methods: {ilMethods.Count}");

            //////////////////////////////////////////////////////////////////////
            // Step 3. Setup forwarding types

            var importer = new ReferenceImporter(
                primaryAssembly.MainModule,
                this.adjustAssemblyReferences);

            var corLibRef = primaryAssembly.MainModule.TypeSystem.CoreLibrary switch
            {
                AssemblyNameReference anr => this.assemblyResolver.Resolve(anr).MainModule,
                ModuleDefinition md => md,
                _ => throw new InvalidOperationException(),
            };
            this.logger.Trace($"Detected primary corlib: {corLibRef.FileName}");

            foreach (var type in
                new[] { corLibRef }.Concat(
                    primaryAssembly.Modules.
                    SelectMany(m => m.AssemblyReferences).
                    SelectMany(anr => this.assemblyResolver.Resolve(anr).Modules)).
                Concat(primaryAssembly.Modules).
                Distinct(new ModuleDefinitionComparer()).
                SelectMany(m => m.Types).
                Where(t =>
                    (t.IsPublic || t.IsNestedPublic) &&
                    (t.BaseType != null || t.FullName == "System.Object")))
            {
                importer.RegisterForwardType(type);
            }

            // HACK: See `IsForwardRef()`
            var methodImplType = importer.GetForwardType(
                "System.Runtime.CompilerServices.MethodImplAttribute");
            var methodImplConstructor = methodImplType.Resolve().Methods.
                Where(m => m.IsConstructor && m.Parameters.Count == 1).
                OrderBy(m => m.Parameters[0].ParameterType.IsPrimitive ? 1 : 0).
                First();

            //////////////////////////////////////////////////////////////////////
            // Step 4. Compose

            var composed = 0;
            foreach (var forwardrefMethod in primaryMethods)
            {
                var fullName = forwardrefMethod.FullName;
                if (ilMethods.TryGetValue(fullName, out var referenceMethod))
                {
                    this.ComposeMethod(
                        forwardrefMethod,
                        referenceMethod,
                        importer,
                        methodImplConstructor);
                    this.logger.Trace($"Composed: {fullName}");
                    composed++;
                }
                else
                {
                    this.logger.Error($"Could not find reference method: {fullName}");
                }
            }

            //////////////////////////////////////////////////////////////////////
            // Step 5. Finish

            if (composed == primaryMethods.Length)
            {
                var targetBasePath = Utilities.GetDirectoryPath(primaryAssemblyPath);

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
                    var primaryDebuggingPath = Path.Combine(
                        Utilities.GetDirectoryPath(primaryAssemblyPath),
                        Path.GetFileNameWithoutExtension(primaryAssemblyPath) + ".pdb");

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

                this.logger.Information($"Assembly composed: {primaryAssemblyPath}, Methods={composed}");

                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
