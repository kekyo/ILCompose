/////////////////////////////////////////////////////////////////////////////////////
//
// ILCompose - Compose partially implementation both .NET language and IL assembler.
// Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)
//
// Licensed under Apache-v2: https://opensource.org/licenses/Apache-2.0
//
/////////////////////////////////////////////////////////////////////////////////////

using System.Collections.Generic;
using System.Linq;

using Mono.Cecil;

namespace ILCompose
{
    internal sealed class ReferenceImporter
    {
        private readonly ModuleDefinition module;
        private readonly bool applyForward;
        private readonly Dictionary<string, TypeReference> forwardTypes = new();

        public ReferenceImporter(ModuleDefinition module, bool applyForward)
        {
            this.module = module;
            this.applyForward = applyForward;
        }

        public void RegisterForwardType(TypeReference type)
        {
            if (!this.forwardTypes.ContainsKey(type.FullName))
            {
                this.forwardTypes.Add(type.FullName, type);
            }
        }

        public TypeReference GetForwardType(string fullName) =>
            this.forwardTypes[fullName];

        public TypeReference Import(TypeReference type)
        {
            if (type is GenericParameter genericParameter)
            {
                if (genericParameter.Owner is MethodReference parentMethod)
                {
                    var importedMethod = this.Import(parentMethod);
                    var importedGenericParameter = importedMethod.GenericParameters[genericParameter.Position];
                    return importedGenericParameter;
                }
                else if (genericParameter.Owner is TypeReference parentType)
                {
                    var importedType = this.Import(parentType);
                    var importedGenericParameter = importedType.GenericParameters[genericParameter.Position];
                    return importedGenericParameter;
                }
            }
            else if (this.applyForward &&
                this.forwardTypes.TryGetValue(type.FullName, out var tf))
            {
                return this.module.ImportReference(tf);
            }

            return this.module.ImportReference(type);
        }

        public MethodReference Import(MethodReference method)
        {
            if (this.applyForward &&
                method.DeclaringType is { } type &&
                forwardTypes.TryGetValue(type.FullName, out var tf))
            {
                var itype = this.module.ImportReference(tf);
                if (itype.Resolve().Methods.
                    FirstOrDefault(m => m.FullName == method.FullName) is { } m)
                {
                    return this.module.ImportReference(m);
                }
            }
            return this.module.ImportReference(method);
        }

        public FieldReference Import(FieldReference field)
        {
            if (this.applyForward &&
                field.DeclaringType is { } type &&
                forwardTypes.TryGetValue(type.FullName, out var tf))
            {
                var itype = this.module.ImportReference(tf);
                if (itype.Resolve().Fields.
                    FirstOrDefault(f => f.FullName == field.FullName) is { } f)
                {
                    return this.module.ImportReference(f);
                }
            }
            return this.module.ImportReference(field);
        }
    }
}
