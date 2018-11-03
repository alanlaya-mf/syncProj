﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Collections;

/// <summary>
/// Helper class for generating solution or projects.
/// </summary>
public class SolutionProjectBuilder
{
    static Solution m_solution = null;
    static Project m_project = null;
    public static String m_workPath;  // Path where we are building solution / project at. By default same as script is started from.
    
    /// <summary>
    /// Relative directory from solution. Set by RunScript.
    /// </summary>
    public static String m_scriptRelativeDir = "";
    static List<String> m_platforms = new List<String>();
    static List<String> m_configurations = new List<String>();
    static Project m_solutionRoot = new Project();
    static String m_groupPath = "";
    private static readonly Destructor Finalise = new Destructor();

    static SolutionProjectBuilder()
    {
        String path = Path2.GetScriptPath(3);

        if (path != null)
            m_workPath = Path.GetDirectoryName(path);
        //Console.WriteLine(m_workPath);
    }

    /// <summary>
    /// Just an indicator that we did not have any exception.
    /// </summary>
    static bool bEverythingIsOk = true;

    /// <summary>
    /// Execute once for each invocation of script. Not executed if multiple scripts are included.
    /// </summary>
    private sealed class Destructor
    {
        ~Destructor()
        {
            try
            {
                if (!bEverythingIsOk)
                    return;
                
                externalproject(null);

                if (m_solution != null)
                    m_solution.SaveSolution();
                else
                    if (m_project != null)
                        m_project.SaveProject();
                    else
                        Console.WriteLine("No solution or project defined. Use project() or solution() functions in script");
            }
            catch (Exception ex)
            {
                ConsolePrintException(ex);
            }
        }
    }

    /// <summary>
    /// Creates new solution.
    /// </summary>
    /// <param name="name">Solution name</param>
    static public void solution(String name)
    {
        m_solution = new Solution();
        m_solution.path = Path.Combine(m_workPath, name);
        if (!m_solution.path.EndsWith(".sln"))
            m_solution.path += ".sln";
    }


    static void requireProjectSelected()
    {
        if (m_project == null)
            throw new Exception2("Project not specified (Use project(\"name\" to specify new project)");
    }

    static void generateConfigurations()
    {
        // Generating configurations for solution
        if (m_project == null)
        {
            if (m_solution == null)
                throw new Exception2("Solution not specified (Use solution(\"name\" to specify new solution)");

            m_solution.configurations.Clear();

            foreach (String platform in m_platforms)
                foreach (String configuration in m_configurations)
                    m_solution.configurations.Add(configuration + "|" + platform);
        }
        else {
            requireProjectSelected();

            m_project.configurations.Clear();

            foreach (String platform in m_platforms)
                foreach (String configuration in m_configurations)
                    m_project.configurations.Add(configuration + "|" + platform);
        }
    }

    /// <summary>
    /// Specify platform list to be used for your solution or project.
    ///     For example: platforms("x32", "x64");
    /// </summary>
    /// <param name="platformList">List of platforms to support</param>
    static public void platforms(params String[] platformList)
    {
        m_platforms = m_platforms.Concat(platformList).Distinct().ToList();
        generateConfigurations();
    }

    /// <summary>
    /// Specify which configurations to support. Typically "Debug" and "Release".
    /// </summary>
    /// <param name="configurationList">Configuration list to support</param>
    static public void configurations(params String[] configurationList)
    {
        m_configurations = m_configurations.Concat(configurationList).Distinct().ToList();
        generateConfigurations();
    }


    /// <summary>
    /// Generates Guid based on String. Key assumption for this algorithm is that name is unique (across where it it's being used)
    /// and if name byte length is less than 16 - it will be fetched directly into guid, if over 16 bytes - then we compute sha-1
    /// hash from string and then pass it to guid.
    /// </summary>
    /// <param name="name">Unique name which is unique across where this guid will be used.</param>
    /// <returns>For example "{706C7567-696E-7300-0000-000000000000}" for "plugins"</returns>
    static public String GenerateGuid(String name)
    {
        byte[] buf = Encoding.UTF8.GetBytes(name);
        byte[] guid = new byte[16];
        if (buf.Length < 16)
        {
            Array.Copy(buf, guid, buf.Length);
        }
        else
        {
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(buf);
                // Hash is 20 bytes, but we need 16. We loose some of "uniqueness", but I doubt it will be fatal
                Array.Copy(hash, guid, 16);
            }
        }

        // Don't use Guid constructor, it tends to swap bytes. We want to preserve original string as hex dump.
        String guidS = "{" + String.Format("{0:X2}{1:X2}{2:X2}{3:X2}-{4:X2}{5:X2}-{6:X2}{7:X2}-{8:X2}{9:X2}-{10:X2}{11:X2}{12:X2}{13:X2}{14:X2}{15:X2}",
            guid[0], guid[1], guid[2], guid[3], guid[4], guid[5], guid[6], guid[7], guid[8], guid[9], guid[10], guid[11], guid[12], guid[13], guid[14], guid[15]) + "}";

        return guidS;
    }


    static void specifyproject(String name)
    {
        if (m_project != null && m_solution != null )
            m_solution.projects.Add(m_project);

        if (name == null)       // Will be used to "flush" last filled project.
            return;

        m_project = new Project() { solution = m_solution };
        m_project.ProjectName = name;
        m_project.language = "C++";
        m_project.RelativePath = Path.Combine(m_scriptRelativeDir, name);

        Project parent = m_solutionRoot;
        String pathSoFar = "";

        foreach (String pathPart in m_groupPath.Split(new char[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
        {
            Project p = parent.nodes.Where(x => x.ProjectName == pathPart && x.RelativePath == pathPart).FirstOrDefault();
            pathSoFar = pathSoFar + ((pathSoFar.Length != 0) ? "/" : "") + pathPart;
            if (p == null)
            {
                p = new Project() { solution = m_solution, ProjectName = pathPart, RelativePath = pathPart, ProjectGuid = GenerateGuid(pathSoFar), bIsFolder = true };
                m_solution.projects.Add(p);
                parent.nodes.Add(p);
                p.parent = parent;
            }

            parent = p;
        }

        parent.nodes.Add(m_project);
        m_project.parent = parent;
    }

    /// <summary>
    /// Add to solution reference to external project
    /// </summary>
    /// <param name="name">Project name</param>
    static public void externalproject(String name)
    {
        specifyproject(name);
    }

    /// <summary>
    /// Adds new project to solution
    /// </summary>
    /// <param name="name">Project name</param>
    static public void project(String name)
    {
        specifyproject(name);
    }


    /// <summary>
    /// The location function sets the destination directory for a generated solution or project file.
    /// </summary>
    /// <param name="path"></param>
    static public void location(String path)
    {
        if (m_project == null)
        {
            m_workPath = path;
        }
        else
        {
            m_project.RelativePath = Path.Combine(path, m_project.ProjectName);
        }
    }

    /// <summary>
    /// Specifies project uuid.
    /// </summary>
    /// <param name="uuid"></param>
    static public void uuid(String uuid)
    {
        requireProjectSelected();

        Guid guid;
        if (!Guid.TryParse(uuid, out guid))
            throw new Exception2("Invalid uuid value '" + uuid + "'");

        m_project.ProjectGuid = "{" + uuid + "}";
    }

    /// <summary>
    /// Sets project programming language (reflects to used project extension)
    /// </summary>
    /// <param name="lang">C++ or C#</param>
    static public void language(String lang)
    {
        requireProjectSelected();

        switch (lang)
        {
            case "C++": m_project.language = lang; break;
            case "C#": m_project.language = lang; break;
            default:
                throw new Exception2("Language '" + lang + "' is not supported");
        } //switch
    }

    /// <summary>
    /// Specify one or more non-linking project build order dependencies.
    /// </summary>
    /// <param name="projectName">project name on which your currently selected project depends on</param>
    static public void dependson(String projectName)
    {
        if (m_project.ProjectDependencies == null)
            m_project.ProjectDependencies = new List<string>();

        m_project.ProjectDependencies.Add(projectName);
    }

    /// <summary>
    /// Sets current "directory" where project should be placed.
    /// </summary>
    /// <param name="groupPath"></param>
    static public void group(String groupPath)
    {
        m_groupPath = groupPath;
    }
    
    /// <summary>
    /// Invokes C# Script by source code path. If any error, exception will be thrown.
    /// </summary>
    /// <param name="path">c# script path</param>
    static public void invokeScript(String path)
    {
        String errors = "";
        if (!CsScript.RunScript(path, true, out errors, "no_exception_handling"))
            throw new Exception(errors);
    }


    /// <summary>
    /// Selected configurations (Either project global or file specific) selected by filter.
    /// selectedFileConfigurations is file specific, selectedConfigurations is project wide.
    /// </summary>
    static List<FileConfigurationInfo> selectedFileConfigurations = new List<FileConfigurationInfo>();
    static List<FileConfigurationInfo> selectedConfigurations = new List<FileConfigurationInfo>();
    static String[] selectedFilters = null;     // null if not set
    static bool bLastSetFilterWasFileSpecific = false;

    /// <summary>
    /// Gets currently selected configurations by filter.
    /// </summary>
    /// <param name="bForceNonFileSpecific">true to force project specific configuration set.</param>
    static List<FileConfigurationInfo> getSelectedConfigurations( bool bForceNonFileSpecific )
    {
        List<FileConfigurationInfo> list;

        if (bForceNonFileSpecific)
            list = selectedConfigurations;
        else
            list = (bLastSetFilterWasFileSpecific) ? selectedFileConfigurations: selectedConfigurations;

        if (list.Count == 0)
            filter();

        return list;
    }

    /// <summary>
    /// Selects to which configurations to apply subsequent function calls (like "kind", "symbols", "files"
    /// and so on...)
    /// </summary>
    /// <param name="filters">
    ///     Either configuration name, for example "Debug" / "Release" or
    ///     by platform name, for example: "platforms:Win32", "platforms:x64"
    ///     or by file name, for example: "files:my.cpp"
    /// </param>
    static public void filter(params String[] filters)
    {
        requireProjectSelected();

        Dictionary2<String, String> dFilt = new Dictionary2<string, string>();

        foreach (String filter in filters)
        {
            String[] v = filter.Split(new char[] { ':' }, 2);

            if (v.Length == 1)
            {
                dFilt["configurations"] = v[0];
            }
            else
            {
                String key = v[0].ToLower();
                if (key != "configurations" && key != "platforms" && key != "files")
                    throw new Exception2("filter tag '" + key + "' is not supported");

                dFilt[key] = v[1];
            } //if-else
        } //for

        IList configItems = m_project.projectConfig;
        Type type = typeof(Configuration);

        if (dFilt.ContainsKey("files"))
        {
            String fName = dFilt["files"];
            FileInfo fileInfo = m_project.files.Where(x => x.relativePath == fName).FirstOrDefault();

            if (fileInfo == null)
                throw new Exception2("File not found: '" + fName + "' - please specify correct filename. Should be registered via 'files' function.");
            configItems = fileInfo.fileConfig;
            type = typeof(FileConfigurationInfo);
            bLastSetFilterWasFileSpecific = true;
        }
        else {
            bLastSetFilterWasFileSpecific = false;
        }

        String confMatchPatten;
        if (dFilt.ContainsKey("configurations"))
            confMatchPatten = dFilt["configurations"];
        else
            confMatchPatten = ".*?";

        confMatchPatten += "\\|";

        if (dFilt.ContainsKey("platforms"))
            confMatchPatten += dFilt["platforms"];
        else
            confMatchPatten += ".*";

        Regex reConfMatch = new Regex(confMatchPatten);
        
        if(bLastSetFilterWasFileSpecific)
            selectedFileConfigurations.Clear();
        else
            selectedConfigurations.Clear();

        for ( int i = 0; i < m_project.configurations.Count; i++ )
        {
            if (reConfMatch.Match(m_project.configurations[i]).Success)
            {
                if (i >= configItems.Count)
                {
                    FileConfigurationInfo fci = (FileConfigurationInfo)Activator.CreateInstance(type);
                    fci.confName = m_project.configurations[i];
                    configItems.Add(fci);
                }
            
                if(bLastSetFilterWasFileSpecific)
                    selectedFileConfigurations.Add((FileConfigurationInfo)configItems[i]);
                else
                    selectedConfigurations.Add((FileConfigurationInfo)configItems[i]);
            }
        } //for

        if (!bLastSetFilterWasFileSpecific)
            selectedFilters = filters;
    } //filter

    /// <summary>
    /// Specifies application type, one of following: 
    /// </summary>
    /// <param name="_kind">
    /// WindowedApp, Application    - Window application<para />
    /// DynamicLibrary, SharedLib   - .dll<para />
    /// </param>
    static public void kind(String _kind)
    {
        EConfigurationType type;
        var enums = Enum.GetValues(typeof(EConfigurationType)).Cast<EConfigurationType>();
        ESubSystem subsystem = ESubSystem.Windows;

        switch (_kind)
        {
            case "ConsoleApp":          type = EConfigurationType.Application; subsystem = ESubSystem.Console; break;
            case "WindowedApp":         type = EConfigurationType.Application; break;
            case "Application":         type = EConfigurationType.Application; break;
            case "SharedLib":           type = EConfigurationType.DynamicLibrary; break;
            case "DynamicLibrary":      type = EConfigurationType.DynamicLibrary; break;
            default:
                throw new Exception2("kind value is not supported '" + _kind + "' - supported values are: " + String.Join(",", enums.Select(x => x.ToString()) ));
        }

        foreach (var conf in getSelectedConfigurations(true).Cast<Configuration>().Where(x => x != null))
        {
            conf.ConfigurationType = type;
            conf.SubSystem = subsystem;
        }
    } //kind

    /// <summary>
    /// Selects the compiler, linker, etc. which are used to build a project or configuration.
    /// </summary>
    /// <param name="toolset">
    /// For example:<para />
    ///     'v140' - for Visual Studio 2015.<para />
    ///     'v120' - for Visual Studio 2013.<para />
    /// </param>
    static public void toolset(String toolset)
    {
        foreach (var conf in getSelectedConfigurations(true).Cast<Configuration>().Where(x => x != null))
            conf.PlatformToolset = toolset;
    }

    /// <summary>
    /// Selects character set.
    /// </summary>
    /// <param name="charset">One of following: "Unicode", "Multibyte", "MBCS"</param>
    static public void characterset(String charset)
    {
        ECharacterSet cs;
        switch (charset.ToLower())
        {
            case "unicode":     cs = ECharacterSet.Unicode;   break;
            case "mbcs":        cs = ECharacterSet.MultiByte; break;
            case "multibyte":   cs = ECharacterSet.MultiByte; break;
            default:
                throw new Exception2("characterset value is not supported '" + charset + "' - supported values are: " + 
                    String.Join(",", Enum.GetValues(typeof(ECharacterSet)).Cast<ECharacterSet>().Select(x => x.ToString())));
        } //switch

        foreach (var conf in getSelectedConfigurations(true).Cast<Configuration>().Where(x => x != null))
            conf.CharacterSet = cs;
    }

    /// <summary>
    /// Specifies output directory.
    /// </summary>
    static public void targetdir(String directory)
    {
        foreach (var conf in getSelectedConfigurations(true).Cast<Configuration>().Where(x => x != null))
            conf.OutDir = directory;
    }

    /// <summary>
    /// Specifies intermediate Directory.
    /// </summary>
    /// <param name="directory">For example "$(Configuration)\" or "obj\$(Platform)\$(Configuration)\"</param>
    static public void objdir(String directory)
    {
        foreach (var conf in getSelectedConfigurations(true).Cast<Configuration>().Where(x => x != null))
            conf.IntDir = directory;
    }

    /// <summary>
    /// Specifies target name. (Filename without extension)
    /// </summary>
    static public void targetname(String name)
    {
        foreach (var conf in getSelectedConfigurations(true).Cast<Configuration>().Where(x => x != null))
            conf.TargetName = name;
    }

    /// <summary>
    /// Specifies target file extension, including comma separator.
    /// </summary>
    /// <param name="extension">For example ".dll", ".exe"</param>
    static public void targetextension(String extension)
    {
        foreach (var conf in getSelectedConfigurations(true).Cast<Configuration>().Where(x => x != null))
            conf.TargetExt = extension;
    }

    /// <summary>
    /// Specifies the #include form of the precompiled header file name.
    /// </summary>
    /// <param name="file">header file</param>
    static public void pchheader(String file)
    {
        foreach (var conf in getSelectedConfigurations(true).Cast<Configuration>().Where(x => x != null))
        {
            conf.PrecompiledHeader = EPrecompiledHeaderUse.Use;
            conf.PrecompiledHeaderFile = file;
        }
    }

    /// <summary>
    /// Specifies the C/C++ source code file which controls the compilation of the header.
    /// </summary>
    /// <param name="file">precompiled source code which needs to be compiled</param>
    static public void pchsource(String file)
    {
        var bkp1 = selectedFileConfigurations;
        var bkp2 = bLastSetFilterWasFileSpecific;

        List<String> fileFilter = new List<string>();
        fileFilter.Add("files:" + file);

        if (selectedFilters != null)
            fileFilter.AddRange(selectedFilters);

        filter(fileFilter.ToArray());

        foreach (var conf in getSelectedConfigurations(false))
            conf.PrecompiledHeader = EPrecompiledHeaderUse.Create;

        selectedFileConfigurations = bkp1;
        bLastSetFilterWasFileSpecific = bkp2;
    } //pchsource

    /// <summary>
    /// Specified whether debug symbols are enabled or not.
    /// </summary>
    /// <param name="value">
    /// "on" - debug symbols are enabled<para />
    /// "off" - debug symbols are disabled<para />
    /// "fastlink" - debug symbols are enabled + faster linking enabled.<para />
    /// </param>
    static public void symbols(String value)
    {
        //
        //  symbols preconfigures multiple flags. premake5 also preconfigures multiple. Maybe later if needs to be separated - create separate functions calls for
        //  each configurable parameter.
        //
        EGenerateDebugInformation d;
        bool bUseDebugLibraries = false;
        switch (value.ToLower())
        {
            case "on":          d = EGenerateDebugInformation.OptimizeForDebugging; bUseDebugLibraries = true;  break;
            case "off":         d = EGenerateDebugInformation.No; bUseDebugLibraries = false; break;
            case "fastlink":    d = EGenerateDebugInformation.OptimizeForFasterLinking; bUseDebugLibraries = true; break;
            default:
                throw new Exception2("Allowed symbols() values are: on, off, fastlink");
        }

        foreach (var conf in getSelectedConfigurations(true).Cast<Configuration>().Where(x => x != null))
        {
            conf.GenerateDebugInformation = d;
            conf.UseDebugLibraries = bUseDebugLibraries;
            optimize_symbols_recheck(conf);
        } //foreach
    }

    /// <summary>
    /// Specifies additional include directories.
    /// </summary>
    /// <param name="dirs">List of additional include directories.</param>
    static public void includedirs(params String[] dirs)
    {
        foreach (var conf in getSelectedConfigurations(false))
        {
            if (conf.AdditionalIncludeDirectories.Length != 0)
                conf.AdditionalIncludeDirectories += ";";

            conf.AdditionalIncludeDirectories += String.Join(";", dirs);
        }
    }

    /// <summary>
    /// Specifies additional defines.
    /// </summary>
    /// <param name="defines">defines, like for example "DEBUG", etc...</param>
    static public void defines(params String[] defines)
    {
        foreach (var conf in getSelectedConfigurations(false))
        {
            if (conf.PreprocessorDefinitions.Length != 0)
                conf.PreprocessorDefinitions += ";";

            conf.PreprocessorDefinitions += String.Join(";", defines);
        }
    }

    /// <summary>
    /// Adds one or more file into project.
    /// </summary>
    /// <param name="files">Files to be added</param>
    static public void files(params String[] files)
    {
        requireProjectSelected();

        foreach (String file in files)
        {
            FileInfo fi = new FileInfo() { relativePath = file };

            switch (Path.GetExtension(file).ToLower())
            {
                case ".c":
                case ".cxx":
                case ".cpp": 
                    fi.includeType = IncludeType.ClCompile; break;

                case ".h":
                    fi.includeType = IncludeType.ClInclude; break;

                case ".rc":
                    fi.includeType = IncludeType.ResourceCompile; break;

                case ".ico":
                    fi.includeType = IncludeType.Image; break;

                case ".txt":
                    fi.includeType = IncludeType.Text; break;
                
                default:
                    fi.includeType = IncludeType.None; break;
            }
            
            m_project.files.Add(fi);
        } //foreach
    } //files

    /// <summary>
    /// Enables certain flags for specific configurations.
    /// </summary>
    /// <param name="flags">
    /// "LinkTimeOptimization" - Enable link-time (i.e. whole program) optimizations.<para />
    /// </param>
    static public void flags(params String[] flags)
    {
        requireProjectSelected();

        foreach (String flag in flags)
        {
            switch (flag.ToLower())
            {
                case "linktimeoptimization":
                    {
                        foreach (var conf in getSelectedConfigurations(true).Cast<Configuration>().Where(x => x != null))
                            conf.WholeProgramOptimization = EWholeProgramOptimization.UseLinkTimeCodeGeneration;
                    }
                    break;
            } //switch 
        } //foreach
    } //flags

    /// <summary>
    /// Adds one or more obj or lib into project to link against.
    /// </summary>
    /// <param name="files"></param>
    static public void links(params String[] files)
    { 
        foreach (var conf in getSelectedConfigurations(false))
        {
            if (conf.AdditionalDependencies.Length != 0)
                conf.AdditionalDependencies += ";";

            conf.AdditionalDependencies += String.Join(";", files);
        }
    } //links


    /// <summary>
    /// Adds one or more library directory from where to search .obj / .lib files.
    /// </summary>
    /// <param name="folders"></param>
    static public void libdirs(params String[] folders)
    {
        foreach (var conf in getSelectedConfigurations(false))
        {
            if (conf.AdditionalLibraryDirectories.Length != 0)
                conf.AdditionalLibraryDirectories += ";";

            conf.AdditionalLibraryDirectories += String.Join(";", folders);
        }
    } //links


    /// <summary>
    /// optimize and symbols reflect to debug format chosen.
    /// </summary>
    /// <param name="fconf"></param>
    static void optimize_symbols_recheck(FileConfigurationInfo fconf)
    {
        Configuration conf = fconf as Configuration;
        if (conf == null) 
            return;     // Project wide configuration only.

        EDebugInformationFormat debugFormat = EDebugInformationFormat.None;
        if (conf.UseDebugLibraries)
        {
            if (conf.Optimization == EOptimization.Full)
            {
                debugFormat = EDebugInformationFormat.ProgramDatabase;
            }
            else
            { 
                debugFormat = EDebugInformationFormat.EditAndContinue;
            }
        }
        else 
        { 
            debugFormat = EDebugInformationFormat.None;
        }

        conf.DebugInformationFormat = debugFormat;
    } //optimize_symbols_recheck


    /// <summary>
    /// Specifies optimization level to be used.
    /// </summary>
    /// <param name="optLevel">Optimization level to enable - one of following: off, size, speed, on(or full)</param>
    static public void optimize(String optLevel)
    {
        EOptimization opt;
        bool bFunctionLevelLinking = true;
        bool bIntrinsicFunctions = true;
        bool bEnableCOMDATFolding = false;
        bool bOptimizeReferences = false;

        switch (optLevel.ToLower())
        {
            case "custom": 
                opt = EOptimization.Custom; 
                break;
            case "off": 
                opt = EOptimization.Disabled; 
                bFunctionLevelLinking = false; 
                bIntrinsicFunctions = false; 
                break;
            case "full":
            case "on": 
                opt = EOptimization.Full; 
                bEnableCOMDATFolding = true; 
                bOptimizeReferences = true;
                break;
            case "size": 
                opt = EOptimization.MinSpace; 
                break;
            case "speed": 
                opt = EOptimization.MaxSpeed;
                break;
            default:
                throw new Exception2("Allowed optimization() values are: off, size, speed, on(or full)");
        }

        foreach (var conf in getSelectedConfigurations(false))
        {
            conf.Optimization = opt;
            conf.FunctionLevelLinking = bFunctionLevelLinking;
            conf.IntrinsicFunctions = bIntrinsicFunctions;
            conf.EnableCOMDATFolding = bEnableCOMDATFolding;
            conf.OptimizeReferences = bOptimizeReferences;
            optimize_symbols_recheck(conf);
        }
    } //optimize

    /// <summary>
    /// Prints more details about given exception. In visual studio format for errors.
    /// </summary>
    /// <param name="ex">Exception occurred.</param>
    static public void ConsolePrintException(Exception ex, String[] args = null)
    {
        if (args != null && args.Contains("no_exception_handling"))
            throw ex;

        Exception2 ex2 = ex as Exception2;
        String fromWhere = "";
        if (ex2 != null)
        {
            StackFrame f = ex2.strace.GetFrame(ex2.strace.FrameCount - 1);
            // Not always can be determined for some reason
            if(f.GetFileName() != null )
                fromWhere = f.GetFileName() + "(" + f.GetFileLineNumber() + "," + f.GetFileColumnNumber() + "): ";
        }

        if(!ex.Message.Contains("error") )
            Console.WriteLine(fromWhere + "error: " + ex.Message);
        else
            Console.WriteLine(ex.Message);

        Console.WriteLine();
        Console.WriteLine("----------------------- Full call stack trace follows -----------------------");
        Console.WriteLine();
        Console.WriteLine(ex.StackTrace);
        Console.WriteLine();
        bEverythingIsOk = false;
    }
};
