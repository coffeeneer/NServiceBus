namespace NServiceBus.Hosting.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;

    /// <summary>
    /// Helpers for assembly scanning operations
    /// </summary>
    public class AssemblyScanner
    {
        readonly List<string> assembliesToSkip = new List<string>();
        readonly List<string> assembliesToInclude = new List<string>();
        readonly string baseDirectoryToScan;

        bool includeAppDomainAssemblies;
        bool recurse;
        bool includeExeInScan;

        public AssemblyScanner(): this(AppDomain.CurrentDomain.BaseDirectory)
        {
        }

        public AssemblyScanner(string baseDirectoryToScan)
        {
            this.baseDirectoryToScan = baseDirectoryToScan;
            
            EnableCompatibilityMode();
        }

        [ObsoleteEx(RemoveInVersion = "5.0", Message = "AssemblyScanner now defaults to work in 'compatibility mode', i.e. it includes subdirs in the scan and picks up .exe files as well. In the future, 'compatibility mode' should be opt-in instead of opt-out")]
        void EnableCompatibilityMode()
        {
            // default
            ScanExeFiles(true);
            ScanSubdirectories(true);

            // possibly opt-out of compatibility mode
            bool compatibilityMode;
            if (bool.TryParse(ConfigurationManager.AppSettings["NServiceBus/AssemblyScanning/CompatibilityMode"],
                              out compatibilityMode))
            {
                if (!compatibilityMode)
                {
                    ScanExeFiles(false);
                    ScanSubdirectories(false);
                }
            }
        }
        
        public AssemblyScanner IncludeAppDomainAssemblies()
        {
            includeAppDomainAssemblies = true;
            return this;
        }

        public AssemblyScanner ScanSubdirectories(bool shouldRecurse)
        {
            recurse = shouldRecurse;
            return this;
        }

        public AssemblyScanner ScanExeFiles(bool shouldScanForExeFiles)
        {
            includeExeInScan = shouldScanForExeFiles;
            return this;
        }

        public AssemblyScanner IncludeAssemblies(IEnumerable<string> assembliesToAddToListOfIncludedAssemblies)
        {
            if (assembliesToAddToListOfIncludedAssemblies != null)
            {
                assembliesToInclude.AddRange(assembliesToAddToListOfIncludedAssemblies);
            }
            return this;
        }

        public AssemblyScanner ExcludeAssemblies(IEnumerable<string> assembliesToAddToListOfSkippedAssemblies)
        {
            if (assembliesToAddToListOfSkippedAssemblies != null)
            {
                assembliesToSkip.AddRange(assembliesToAddToListOfSkippedAssemblies);
            }
            return this;
        }

        /// <summary>
        /// Traverses the specified base directory including all subdirectories, generating a list of assemblies that can be
        /// scanned for handlers, a list of skipped files, and a list of errors that occurred while scanning.
        /// Scanned files may be skipped when they're either not a .NET assembly, or if a reflection-only load of the .NET assembly
        /// reveals that it does not reference NServiceBus.
        /// </summary>
        [DebuggerNonUserCode]
        public AssemblyScannerResults GetScannableAssemblies()
        {
            var results = new AssemblyScannerResults();

            if (includeAppDomainAssemblies)
            {
                var matchingAssembliesFromAppDomain = AppDomain.CurrentDomain
                                                           .GetAssemblies()
                                                           .Where(assembly => IsIncluded(assembly.GetName().Name))
                                                           .ToArray();

                results.Assemblies.AddRange(matchingAssembliesFromAppDomain);
            }

            var assemblyFiles = ScanDirectoryForAssemblyFiles();

            foreach (var assemblyFile in assemblyFiles)
            {
                Assembly assembly;

                if (!IsIncluded(assemblyFile.Name))
                {
                    results.SkippedFiles.Add(new SkippedFile(assemblyFile.FullName, "File was explicitly excluded from scanning"));
                    continue;
                }

                var compilationMode = Image.GetCompilationMode(assemblyFile.FullName);
                if (compilationMode == CompilationMode.NativeOrInvalid)
                {
                    results.SkippedFiles.Add(new SkippedFile(assemblyFile.FullName, "File is not a .NET assembly"));
                    continue;
                }

                if (!Environment.Is64BitProcess && compilationMode == CompilationMode.CLRx64)
                {
                    results.SkippedFiles.Add(new SkippedFile(assemblyFile.FullName, "x64 .NET assembly can't be loaded by a 32Bit process"));
                    continue;
                }

                try
                {
                    if (!AssemblyReferencesNServiceBus(assemblyFile))
                    {
                        results.SkippedFiles.Add(new SkippedFile(assemblyFile.FullName, "Assembly does not reference NServiceBus and thus cannot contain any handlers"));
                        continue;
                    }

                    assembly = Assembly.LoadFrom(assemblyFile.FullName);
                }
                catch (BadImageFormatException badImageFormatException)
                {
                    var errorMessage = string.Format("Could not load {0}. Consider using 'Configure.With(AllAssemblies.Except(\"{1}\"))'" +
                                                     " to tell NServiceBus not to load this file.", assemblyFile.FullName, assemblyFile.Name);
                    var error = new ErrorWhileScanningAssemblies(badImageFormatException, errorMessage);
                    results.Errors.Add(error);
                    continue;
                }

                try
                {
                    //will throw if assembly cannot be loaded
                    assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    var sb = new StringBuilder();
                    sb.AppendFormat("Could not scan assembly: {0}. Exception message {1}.", assemblyFile.FullName, e);
                    if (e.LoaderExceptions.Any())
                    {
                        sb.Append(Environment.NewLine + "Scanned type errors: ");
                        foreach (var ex in e.LoaderExceptions)
                        {
                            sb.Append(Environment.NewLine + ex.Message);
                        }
                    }
                    var error = new ErrorWhileScanningAssemblies(e, sb.ToString());
                    results.Errors.Add(error);
                    continue;
                }

                results.Assemblies.Add(assembly);
            }

            return results;
        }

        IEnumerable<FileInfo> ScanDirectoryForAssemblyFiles()
        {
            var baseDir = new DirectoryInfo(baseDirectoryToScan);
            var searchOption = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var assemblyFiles = GetFileSearchPatternsToUse()
                .SelectMany(extension => baseDir.GetFiles(extension, searchOption))
                .ToList();

            return assemblyFiles;
        }

        IEnumerable<string> GetFileSearchPatternsToUse()
        {
            yield return "*.dll";

            if (includeExeInScan)
            {
                yield return "*.exe";
            }
        }

        bool AssemblyReferencesNServiceBus(FileSystemInfo assemblyFile)
        {
            var lightLoad = Assembly.ReflectionOnlyLoadFrom(assemblyFile.FullName);
            var referencedAssemblies = lightLoad.GetReferencedAssemblies();

            var nameOfAssemblyDefiningHandlersInterface =
                typeof(IHandleMessages<>).Assembly.GetName().Name;

            return referencedAssemblies
                .Any(a => a.Name == nameOfAssemblyDefiningHandlersInterface);
        }
        
        /// <summary>
        /// Determines whether the specified assembly name or file name can be included, given the set up include/exclude
        /// patterns and default include/exclude patterns
        /// </summary>
        bool IsIncluded(string assemblyNameOrFileName)
        {
            var isExplicitlyExcluded = assembliesToSkip.Any(excluded => IsMatch(excluded, assemblyNameOrFileName));

            if (isExplicitlyExcluded)
                return false;

            var noAssembliesWereExplicitlyIncluded = !assembliesToInclude.Any();
            var isExplicitlyIncluded = assembliesToInclude.Any(included => IsMatch(included, assemblyNameOrFileName));

            return noAssembliesWereExplicitlyIncluded || isExplicitlyIncluded;
        }

        static bool IsMatch(string expression, string scopedNameOrFileName)
        {
            if (DistillLowerAssemblyName(scopedNameOrFileName).StartsWith(expression.ToLower()))
                return true;

            if (DistillLowerAssemblyName(expression).TrimEnd('.') == DistillLowerAssemblyName(scopedNameOrFileName))
                return true;

            return false;
        }

        public static bool IsAllowedType(Type type)
        {
            return !type.IsValueType;
        }

        static string DistillLowerAssemblyName(string assemblyOrFileName)
        {
            var lowerAssemblyName = assemblyOrFileName.ToLowerInvariant();
            if (lowerAssemblyName.EndsWith(".dll"))
            {
                lowerAssemblyName = lowerAssemblyName.Substring(0, lowerAssemblyName.Length - 4);
            }
            return lowerAssemblyName;
        }

        // Code kindly provided by the mono project: https://github.com/jbevain/mono.reflection/blob/master/Mono.Reflection/Image.cs
        // Image.cs
        //
        // Author:
        //   Jb Evain (jbevain@novell.com)
        //
        // (C) 2009 - 2010 Novell, Inc. (http://www.novell.com)
        //
        // Permission is hereby granted, free of charge, to any person obtaining
        // a copy of this software and associated documentation files (the
        // "Software"), to deal in the Software without restriction, including
        // without limitation the rights to use, copy, modify, merge, publish,
        // distribute, sublicense, and/or sell copies of the Software, and to
        // permit persons to whom the Software is furnished to do so, subject to
        // the following conditions:
        //
        // The above copyright notice and this permission notice shall be
        // included in all copies or substantial portions of the Software.
        //
        // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
        // EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
        // MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
        // NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
        // LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
        // OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
        // WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
        class Image : IDisposable
        {
            readonly long positionWhenCreated;
            readonly Stream stream;

            public static CompilationMode GetCompilationMode(string file)
            {
                if (file == null)
                    throw new ArgumentNullException("file");

                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return GetCompilationMode(stream);
                }
            }

            static CompilationMode GetCompilationMode(Stream stream)
            {
                if (stream == null)
                    throw new ArgumentNullException("stream");
                if (!stream.CanRead)
                    throw new ArgumentException("Can not read from stream");
                if (!stream.CanSeek)
                    throw new ArgumentException("Can not seek in stream");

                using (var image = new Image(stream))
                {
                    return image.GetCompilationMode();
                }
            }

            Image(Stream stream)
            {
                this.stream = stream;
                positionWhenCreated = stream.Position;
                this.stream.Position = 0;
            }

            CompilationMode GetCompilationMode()
            {
                if (stream.Length < 318)
                    return CompilationMode.NativeOrInvalid;
                if (ReadUInt16() != 0x5a4d)
                    return CompilationMode.NativeOrInvalid;
                if (!Advance(58))
                    return CompilationMode.NativeOrInvalid;
                if (!MoveTo(ReadUInt32()))
                    return CompilationMode.NativeOrInvalid;
                if (ReadUInt32() != 0x00004550)
                    return CompilationMode.NativeOrInvalid;
                if (!Advance(20))
                    return CompilationMode.NativeOrInvalid;

                var result = CompilationMode.NativeOrInvalid;
                switch (ReadUInt16())
                {
                    case 0x10B:
                        if (Advance(206))
                        {
                            result = CompilationMode.CLRx86;
                        }
                        
                        break;
                    case 0x20B:
                        if (Advance(222))
                        {
                            result = CompilationMode.CLRx64;
                        }
                        break;
                }

                if (result == CompilationMode.NativeOrInvalid)
                {
                    return result;
                }

                return ReadUInt32() != 0 ? result : CompilationMode.NativeOrInvalid;
            }

            bool Advance(int length)
            {
                if (stream.Position + length >= stream.Length)
                    return false;

                stream.Seek(length, SeekOrigin.Current);
                return true;
            }

            bool MoveTo(uint position)
            {
                if (position >= stream.Length)
                    return false;

                stream.Position = position;
                return true;
            }

            void IDisposable.Dispose()
            {
                stream.Position = positionWhenCreated;
            }

            ushort ReadUInt16()
            {
                return (ushort)(stream.ReadByte()
                                | (stream.ReadByte() << 8));
            }

            uint ReadUInt32()
            {
                return (uint)(stream.ReadByte()
                              | (stream.ReadByte() << 8)
                              | (stream.ReadByte() << 16)
                              | (stream.ReadByte() << 24));
            }
        }

        public enum CompilationMode
        {
            NativeOrInvalid,
            CLRx86,
            CLRx64
        }
    }
}