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
        private readonly Dictionary<string, TypeReference> typeForwards = new();

        public ReferenceImporter(ModuleDefinition module) =>
            this.module = module;

        public void RegisterForward(TypeReference type)
        {
            if (!typeForwards.ContainsKey(type.Name))
            {
                typeForwards.Add(type.FullName, type);
            }
        }

        public TypeReference Import(TypeReference type) =>
            this.module.ImportReference(
                typeForwards.TryGetValue(type.FullName, out var tf) ? tf : type);

        public MethodReference Import(MethodReference method)
        {
            if (method.DeclaringType is { } type &&
                typeForwards.TryGetValue(type.FullName, out var tf))
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
            if (field.DeclaringType is { } type &&
                typeForwards.TryGetValue(type.FullName, out var tf))
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
