# ILCompose

![ILCompose](Images/ILCompose.100.png)

[![Project Status: WIP – Initial development is in progress, but there has not yet been a stable, usable release suitable for the public.](https://www.repostatus.org/badges/latest/wip.svg)](https://www.repostatus.org/#wip)
[![NuGet ILCompose](https://img.shields.io/nuget/v/ILCompose.svg?style=flat)](https://www.nuget.org/packages/ILCompose)
[![CI build (main)](https://github.com/kekyo/ILCompose/workflows/.NET/badge.svg?branch=main)](https://github.com/kekyo/ILCompose/actions?query=branch%3Amain)

----

## What is this?

Compose partially implementation both .NET language and IL assembler.
An improved implementation of the principles of [ILSupport](https://github.com/ins0mniaque/ILSupport).
If you are looking for an inline assembler for CIL/MSIL, here is a realistic solution.

In sample.cs:

```csharp
namespace IL2C.BasicTypes
{
    public class System_Boolean
    {
        // Refer IL assembly
        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern bool ValueTypeTest();
    }
}
```

In sample.il:
```
.class public IL2C.BasicTypes.System_Boolean
{
    .method public static void ValueTypeTest() cil managed
    {
        .maxstack 2
        ldc.i4.s 123
        box bool

        ; ...

        ret
    }
}
```

These source code compose into one assembly by ILCompose.
Semantically, it is similar to the C# partial class definition, but it does this at the CIL level.

All CIL assemble source code (*.il) are automatically assembled by official dotnet IL assembler `ILAsm`.

Only you have to do install the NuGet package [ILCompose](https://www.nuget.org/packages/ILCompose) and ready to use!

## Environments

Supported target platforms:

* .NET 6, 5
* .NET Core 3.1 to 1.0
* .NET Framework 4.8 to 2.0 (maybe to 1.0)

Supported building platforms:

* dotnet SDK 6, 5, 3.1, 2.2 and 2.1
* .NET Framework 4.8 to 4.6.2

----

## Background

This project was created in [IL2C](https://github.com/kekyo/IL2C.git),
there was a need for IL code management.
In IL2C, CIL unit test code was synthesized into .NET assemblies using ILSupport, but:

* The portion of [NUnit](https://nunit.org/) that relies on
  [Custom attributes extensions](https://docs.nunit.org/articles/nunit/extending-nunit/Custom-Attributes.html) has
  caused problems on the JetBrains Rider's test explorer, and I wanted to eliminate this.
* To solve the above, needed to resolve a problem with custom attributes being removed
  by the `forwardref` attribute in ILSupport.

Related: [IL2C issue #100: Will upgrade basis environments.](https://github.com/kekyo/IL2C/issues/100)

Therefore, I developed this package as a general-purpose package.

## Note

ILSupport is cumbersome because it requires custom build scripts to be incorporated into the your project.
However, ILCompose is simply installation the NuGet package and builds everything automatically.

When you install the NuGet package,
the CIL (*.il) file will have the `ILComposeTargetIL` build action set as follows:

![ILComposeTargetIL](Images/vsproperties.png)

Basically, it can be used in the same way as ILSupport.

ILSupport is realized using ILDasm and a string substitution technique.
While ILCompose is realized using cecil, so the internal implementations are completely different.

Here is where the problem was in ILSupport.
The following C# code with the `forwardref` attribute applied cannot apply the additional custom attributes
(they must be written on the IL assembly source code side):

```csharp
// Refer IL assembly, all additional custom attribute will silently remove by ILSupport.
[MethodImpl(MethodImplOptions.ForwardRef)]
public static extern bool ValueTypeTest();
```

It allows you to apply custom attributes normally, as shown below:

```csharp
// Refer IL assembly with custom attributes
[Test]
[Description("A value type test.")]
[MethodImpl(MethodImplOptions.ForwardRef)]
public static extern void ValueTypeTest();
```

----

## License

Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)

License under Apache-v2.
