﻿/////////////////////////////////////////////////////////////////////////////////////
//
// ILCompose - Compose partially implementation both .NET language and IL assembler.
// Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)
//
// Licensed under Apache-v2: https://opensource.org/licenses/Apache-2.0
//
/////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Mono.Options;

namespace ILCompose
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                var referenceBasePaths = new string[0];
                var adjustAssemblyReferences = false;
                var logLevel = LogLevels.Information;
                var logtfm = default(string);
                var launchDebugger = false;
                var help = false;

                var options = new OptionSet()
                {
                    { "refs=", "Assembly reference base paths", v => referenceBasePaths = v.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries) },
                    { "adjustAssemblyRefs", "Automatic adjust corlib reference", _ => adjustAssemblyReferences = true },
                    { "logLevel=", "Log level [debug|trace|information|warning|error|silent]", v => logLevel = Enum.TryParse<LogLevels>(v, true, out var ll) ? ll : LogLevels.Information },
                    { "logtfm=", "Log header tfm", v => logtfm = v },
                    { "launchDebugger", "Launch debugger", _ => launchDebugger = true },
                    { "h|help", "Print this help", _ => help = true },
                };

                var extra = options.Parse(args);

                if (launchDebugger)
                {
                    Debugger.Launch();
                }

                if (help || (extra.Count < 2))
                {
                    Console.Out.WriteLine($"ILCompose [{ThisAssembly.AssemblyVersion}]");
                    Console.Out.WriteLine("Compose partially implementation both .NET language and IL assembler.");
                    Console.Out.WriteLine("Copyright (c) Kouji Matsui.");
                    Console.Out.WriteLine("usage: ilcompose.exe [options] <primary_assembly_path> <reference_assembly_path> ...");
                    options.WriteOptionDescriptions(Console.Out);
                    return 0;
                }

                using var logger = new TextWriterLogger(
                    logLevel, Console.Out, logtfm);

                logger.Information($"Started.");

                var fullPaths = extra.Select(Path.GetFullPath).ToArray();

                var primaryAssemblyPath = fullPaths[0];
                var referenceAssemblyPaths = fullPaths.
                    Skip(1).
                    ToArray();
                var overallReferenceBasePaths = fullPaths.
                    Select(p => Path.GetDirectoryName(p) ?? ".").
                    Concat(referenceBasePaths.Select(Path.GetFullPath)).
                    Distinct().
                    ToArray();

                var composer = new Composer(logger, overallReferenceBasePaths, adjustAssemblyReferences);
                if (composer.Compose(primaryAssemblyPath, referenceAssemblyPaths))
                {
                    return 0;
                }
                else
                {
                    return -1;
                }
            }
            catch (OptionException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return Marshal.GetHRForException(ex);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return Marshal.GetHRForException(ex);
            }
        }
    }
}