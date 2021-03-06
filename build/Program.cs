﻿using System;
using System.IO;
using System.Linq;
using static Bullseye.Targets;
using static SimpleExec.Command;

namespace build
{
    class Program
    {
        private const string ArtifactsDir = "artifacts";
        private const string Clean = "clean";
        private const string Build = "build";
        private const string Test = "test";
        private const string Pack = "pack";
        private const string Publish = "publish";

        static void Main(string[] args)
        {
            Target(Clean, () =>
            {
                if (!Directory.Exists(ArtifactsDir))
                {
                    return;
                }
                var filesToDelete = Directory
                    .GetFiles(ArtifactsDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith(".gitignore"));
                foreach (var file in filesToDelete)
                {
                    Console.WriteLine($"Deleting file {file}");
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                var directoriesToDelete = Directory.GetDirectories("artifacts");
                foreach (var directory in directoriesToDelete)
                {
                    Console.WriteLine($"Deleting directory {directory}");
                    Directory.Delete(directory, true);
                }
            });

            Target(Build, () => Run("dotnet", "build LittleForker.sln -c Release"));

            Target(Test,
                DependsOn(Build),
                () => Run("dotnet", $"test src/LittleForker.Tests/LittleForker.Tests.csproj -c Release -r {ArtifactsDir} --no-build -l trx;LogFileName=LittleForker.Tests.xml --verbosity=normal"));

            Target(Pack,
                DependsOn(Build),
                () => Run("dotnet", $"pack src/LittleForker/LittleForker.csproj -c Release -o {ArtifactsDir} --no-build"));

            Target(Publish, DependsOn(Pack), () =>
            {
                var packagesToPush = Directory.GetFiles(ArtifactsDir, "*.nupkg", SearchOption.TopDirectoryOnly);
                Console.WriteLine($"Found packages to publish: {string.Join("; ", packagesToPush)}");

                var apiKey = Environment.GetEnvironmentVariable("FEEDZ_LITTLEFORKER_API_KEY");

                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    Console.WriteLine("Feedz API key not available. Packages will not be pushed.");
                    return;
                }
                Console.WriteLine($"Feedz API Key available '{apiKey.Substring(0, 5)}...'. Pushing packages to Feedz...");
                foreach (var packageToPush in packagesToPush)
                {
                    Run("dotnet", $"nuget push {packageToPush} -s https://f.feedz.io/dh/oss-ci/nuget/index.json -k {apiKey} --skip-duplicate", noEcho: true);
                }
            });

            Target("default", DependsOn(Clean, Test, Publish));

            RunTargetsAndExit(args);
        }
    }
}
