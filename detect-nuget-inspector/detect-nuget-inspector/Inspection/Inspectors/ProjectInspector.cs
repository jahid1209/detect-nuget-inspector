﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Synopsys.Detect.Nuget.Inspector.DependencyResolution.Nuget;
using Synopsys.Detect.Nuget.Inspector.Inspection.Util;
using Synopsys.Detect.Nuget.Inspector.Model;
using Synopsys.Detect.Nuget.Inspector.DependencyResolution.PackagesConfig;
using Synopsys.Detect.Nuget.Inspector.DependencyResolution.Project;

namespace Synopsys.Detect.Nuget.Inspector.Inspection.Inspectors
{
    class ProjectInspector : IInspector
    {
        public ProjectInspectionOptions Options;
        public NugetSearchService NugetService;

        public ProjectInspector(ProjectInspectionOptions options, NugetSearchService nugetService)
        {
            Options = options;
            NugetService = nugetService;
            if (Options == null)
            {
                throw new Exception("Must provide a valid options object.");
            }

            if (String.IsNullOrWhiteSpace(Options.ProjectDirectory))
            {
                Options.ProjectDirectory = Directory.GetParent(Options.TargetPath).FullName;
            }

            if (String.IsNullOrWhiteSpace(Options.PackagesConfigPath))
            {
                Options.PackagesConfigPath = CreateProjectPackageConfigPath(Options.ProjectDirectory);
            }

            if (String.IsNullOrWhiteSpace(Options.ProjectJsonPath))
            {
                Options.ProjectJsonPath = CreateProjectJsonPath(Options.ProjectDirectory);
            }

            if (String.IsNullOrWhiteSpace(Options.ProjectJsonLockPath))
            {
                Options.ProjectJsonLockPath = CreateProjectJsonLockPath(Options.ProjectDirectory);
            }

            if (String.IsNullOrWhiteSpace(Options.ProjectAssetsJsonPath))
            {
                Options.ProjectAssetsJsonPath = CreateProjectAssetsJsonPath(Options.ProjectDirectory);
            }

            if (String.IsNullOrWhiteSpace(Options.ProjectName))
            {
                Options.ProjectName = Path.GetFileNameWithoutExtension(Options.TargetPath);
            }

            if (String.IsNullOrWhiteSpace(Options.ProjectUniqueId))
            {
                Options.ProjectUniqueId = Path.GetFileNameWithoutExtension(Options.TargetPath);
            }

            if (String.IsNullOrWhiteSpace(Options.VersionName))
            {
                Options.VersionName = InspectorUtil.GetProjectAssemblyVersion(Options.ProjectDirectory);
            }
        }

        public InspectionResult Inspect()
        {

            try
            {
                Container container = GetContainer();
                List<Container> containers = new List<Container>() { container };
                return new InspectionResult()
                {
                    Status = InspectionResult.ResultStatus.Success,
                    ResultName = Options.ProjectUniqueId,
                    OutputDirectory = Options.OutputDirectory,
                    Containers = containers
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine("{0}", ex.ToString());
                if (Options.IgnoreFailure)
                {
                    Console.WriteLine("Error collecting dependencyinformation on project {0}, cause: {1}", Options.ProjectName, ex);
                    return new InspectionResult()
                    {
                        Status = InspectionResult.ResultStatus.Success,
                        ResultName = Options.ProjectUniqueId,
                        OutputDirectory = Options.OutputDirectory
                    };
                }
                else
                {
                    return new InspectionResult()
                    {
                        Status = InspectionResult.ResultStatus.Error,
                        Exception = ex
                    };
                }
            }

        }

        public Container GetContainer()
        {
            if (IsExcluded())
            {
                Console.WriteLine("Project {0} excluded from task", Options.ProjectName);
                return null;
            }
            else
            {
                //TODO: Move to a new class? ResolutionDispatch?
                var stopWatch = Stopwatch.StartNew();
                Console.WriteLine("Processing Project: {0}", Options.ProjectName);
                if (Options.ProjectDirectory != null)
                {
                    Console.WriteLine("Using Project Directory: {0}", Options.ProjectDirectory);
                }
                Container projectNode = new Container();
                projectNode.Name = Options.ProjectUniqueId;
                projectNode.Version = Options.VersionName;
                projectNode.SourcePath = Options.TargetPath;
                projectNode.Type = "Project";

                try
                {
                    projectNode.OutputPaths = FindOutputPaths();
                }
                catch (Exception e)
                {
                    Console.WriteLine("Unable to determine output paths for this project.");
                }


                bool packagesConfigExists = !String.IsNullOrWhiteSpace(Options.PackagesConfigPath) && File.Exists(Options.PackagesConfigPath);
                bool projectJsonExists = !String.IsNullOrWhiteSpace(Options.ProjectJsonPath) && File.Exists(Options.ProjectJsonPath);
                bool projectJsonLockExists = !String.IsNullOrWhiteSpace(Options.ProjectJsonLockPath) && File.Exists(Options.ProjectJsonLockPath);
                bool projectAssetsJsonExists = !String.IsNullOrWhiteSpace(Options.ProjectAssetsJsonPath) && File.Exists(Options.ProjectAssetsJsonPath);

                if (packagesConfigExists)
                {
                    Console.WriteLine("Using packages config: " + Options.PackagesConfigPath);
                    var packagesConfigResolver = new PackagesConfigResolver(Options.PackagesConfigPath, NugetService);
                    var packagesConfigResult = packagesConfigResolver.Process();
                    projectNode.Packages = packagesConfigResult.Packages;
                    projectNode.Dependencies = packagesConfigResult.Dependencies;
                }
                else if (projectJsonLockExists)
                {
                    Console.WriteLine("Using json lock: " + Options.ProjectJsonLockPath);
                    var projectJsonLockResolver = new ProjectLockJsonResolver(Options.ProjectJsonLockPath);
                    var projectJsonLockResult = projectJsonLockResolver.Process();
                    projectNode.Packages = projectJsonLockResult.Packages;
                    projectNode.Dependencies = projectJsonLockResult.Dependencies;
                }
                else if (projectAssetsJsonExists)
                {
                    Console.WriteLine("Using assets json file: " + Options.ProjectAssetsJsonPath);
                    var projectAssetsJsonResolver = new ProjectAssetsJsonResolver(Options.ProjectAssetsJsonPath);
                    var projectAssetsJsonResult = projectAssetsJsonResolver.Process();
                    projectNode.Packages = projectAssetsJsonResult.Packages;
                    projectNode.Dependencies = projectAssetsJsonResult.Dependencies;
                }
                else if (projectJsonExists)
                {
                    Console.WriteLine("Using project json: " + Options.ProjectJsonPath);
                    var projectJsonResolver = new ProjectJsonResolver(Options.ProjectName, Options.ProjectJsonPath);
                    var projectJsonResult = projectJsonResolver.Process();
                    projectNode.Packages = projectJsonResult.Packages;
                    projectNode.Dependencies = projectJsonResult.Dependencies;
                }
                else
                {
                    Console.WriteLine("Attempting reference resolver: " + Options.TargetPath);
                    var referenceResolver = new ProjectReferenceResolver(Options.TargetPath, NugetService);
                    var projectReferencesResult = referenceResolver.Process();
                    if (projectReferencesResult.Success)
                    {
                        Console.WriteLine("Reference resolver succeeded.");
                        projectNode.Packages = projectReferencesResult.Packages;
                        projectNode.Dependencies = projectReferencesResult.Dependencies;
                    }
                    else
                    {
                        Console.WriteLine("Using backup XML resolver.");
                        var xmlResolver = new ProjectXmlResolver(Options.TargetPath, NugetService);
                        var xmlResult = xmlResolver.Process();
                        projectNode.Version = xmlResult.ProjectVersion;
                        projectNode.Packages = xmlResult.Packages;
                        projectNode.Dependencies = xmlResult.Dependencies;
                    }
                }

                if (projectNode != null && projectNode.Dependencies != null && projectNode.Packages != null)
                {
                    Console.WriteLine("Found {0} dependencies among {1} packages.", projectNode.Dependencies.Count, projectNode.Packages.Count);
                }
                Console.WriteLine("Finished processing project {0} which took {1} ms.", Options.ProjectName, stopWatch.ElapsedMilliseconds);

                return projectNode;
            }
        }


        public List<String> FindOutputPaths()
        {
            //TODO: Move to a new class (OutputPathResolver?)
            Console.WriteLine("Attempting to parse configuration output paths.");
            Console.WriteLine("Project File: " + Options.TargetPath);
            try
            {
                Microsoft.Build.Evaluation.Project proj = new Microsoft.Build.Evaluation.Project(Options.TargetPath);
                List<string> outputPaths = new List<string>();
                List<string> configurations;
                proj.ConditionedProperties.TryGetValue("Configuration", out configurations);
                if (configurations == null) configurations = new List<string>();
                foreach (var config in configurations)
                {
                    proj.SetProperty("Configuration", config);
                    proj.ReevaluateIfNecessary();
                    var path = proj.GetPropertyValue("OutputPath");
                    var fullPath = PathUtil.Combine(proj.DirectoryPath, path);
                    outputPaths.Add(fullPath);
                    Console.WriteLine("Found path: " + fullPath);
                }
                Microsoft.Build.Evaluation.ProjectCollection.GlobalProjectCollection.UnloadProject(proj);
                Console.WriteLine($"Found {outputPaths.Count} paths.");
                return outputPaths;
            }
            catch (Exception e)
            {
                Console.WriteLine("Skipping configuration output paths.");
                return new List<string>() { };
            }
        }

        public bool IsExcluded()
        {
            if (String.IsNullOrWhiteSpace(Options.IncludedModules) && String.IsNullOrWhiteSpace(Options.ExcludedModules))
            {
                return false;
            };

            String projectName = Options.ProjectName.Trim();
            if (!String.IsNullOrWhiteSpace(Options.IncludedModules))
            {
                ISet<string> includedSet = new HashSet<string>();
                string[] projectPatternArray = Options.IncludedModules.Split(new char[] { ',' });
                foreach (string projectPattern in projectPatternArray)
                {
                    if (projectPattern.Trim() == projectName) // legacy behaviour, match if equals with trim.
                    {
                        return false;
                    }
                    try
                    {
                        Match patternMatch = Regex.Match(projectName, projectPattern, RegexOptions.None, TimeSpan.FromMinutes(1));
                        if (patternMatch.Success)
                        {
                            return false;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Unable to parse " + projectPattern + " as a regular expression, so pattern matching module names could not occur.");
                        Console.WriteLine("It is still compared to the project name. To use it as a pattern please fix the following issue:");
                        Console.WriteLine(e);
                    }
                }
                return true;//did not match any inclusion, exclude it.
            }
            else
            {
                ISet<string> excludedSet = new HashSet<string>();
                string[] projectPatternArray = Options.ExcludedModules.Split(new char[] { ',' });
                foreach (string projectPattern in projectPatternArray)
                {
                    if (projectPattern.Trim() == projectName) // legacy behaviour, match if equals with trim.
                    {
                        return true;
                    }
                    try
                    {
                        Match patternMatch = Regex.Match(projectName, projectPattern, RegexOptions.None, TimeSpan.FromMinutes(1));
                        if (patternMatch.Success)
                        {
                            return true;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Unable to parse " + projectPattern + " as a regular expression, so pattern matching module names could not occur.");
                        Console.WriteLine("It is still compared to the project name. To use it as a pattern please fix the following issue:");
                        Console.WriteLine(e);
                    }
                }
                return false;//did not match exclusion, include it.
            }
        }


        private string CreateProjectPackageConfigPath(string projectDirectory)
        {
            return PathUtil.Combine(projectDirectory, "packages.config");
        }

        private string CreateProjectJsonPath(string projectDirectory)
        {
            return PathUtil.Combine(projectDirectory, "project.json");
        }

        private string CreateProjectJsonLockPath(string projectDirectory)
        {
            return PathUtil.Combine(projectDirectory, "project.lock.json");
        }

        private string CreateProjectAssetsJsonPath(string projectDirectory)
        {
            return PathUtil.Combine(projectDirectory, "obj", "project.assets.json");
        }

    }
}
