using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CodeAnalysisApp1
{
    public class QueryChecker
    {
        private readonly string _sqlTag = "sql";
        private readonly string _sqlConnectionStringFile = "sqlcheck.conf";
        public QueryChecker()
        {
            var visualStudioInstances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
            var instance = visualStudioInstances[1];
            Console.WriteLine($"Using MSBuild at '{instance.MSBuildPath}' to load projects.");

            // NOTE: Be sure to register an instance with the MSBuildLocator 
            //       before calling MSBuildWorkspace.Create()
            //       otherwise, MSBuildWorkspace won't MEF compose.
            MSBuildLocator.RegisterInstance(instance);
        }

        private async Task<Solution> OpenSolution(MSBuildWorkspace workspace, string solutionPath)
        {
            // Print message for WorkspaceFailed event to help diagnosing project load failures.
            workspace.WorkspaceFailed += (o, e) => Console.WriteLine(e.Diagnostic.Message);

            Console.WriteLine($"Loading solution '{solutionPath}'");

            // Attach progress reporter so we print projects as they are loaded.
            var solution = await workspace.OpenSolutionAsync(solutionPath);
            Console.WriteLine($"Finished loading solution '{solutionPath}'");
            return solution;
        }

        // return first sqlchecker.conf
        private async Task<string> FindConnectionString(Solution solution)
        {
            foreach (var project in solution.Projects)
            {
                var confFilePath = Path.Combine(Path.GetDirectoryName(project.FilePath), _sqlConnectionStringFile);
                if (File.Exists(confFilePath))
                {
                    var connectionString = await File.ReadAllTextAsync(confFilePath);
                    Console.WriteLine($"SQLCheck connection string found : {connectionString}");
                    return connectionString;
                }
            }
            throw new Exception($"File {_sqlConnectionStringFile} not found in {solution.FilePath}");
        }

        private async Task<IEnumerable<string>> FindSql(Solution solution)
        {
            var sqlStrings = new List<string>();
            foreach (var project in solution.Projects)
            {
                foreach (var document in project.Documents)
                {
                    var model = await document.GetSemanticModelAsync();
                    var variableDeclarations = model.SyntaxTree.GetRoot().DescendantNodes().OfType<VariableDeclarationSyntax>();
                    foreach (var variableDeclaration in variableDeclarations)
                    {
                        sqlStrings.AddRange(variableDeclaration.Variables.Where(v => v.Identifier.Value.ToString().ToLower().Contains(_sqlTag)).Select(v => string.Concat(v.DescendantTokens().Where(t => t.Kind() == SyntaxKind.StringLiteralToken).Select(t => (string)t.Value))));
                    }
                }
            }
            foreach (var sqlString in sqlStrings)
            {
                Console.WriteLine(sqlString);
            }
            return sqlStrings;
        }

        public async Task<QueryChecker> Create(string solutionPath)
        {
            using var workspace = MSBuildWorkspace.Create();
            var solution = await OpenSolution(workspace, solutionPath);
            var connectionString = await FindConnectionString(solution);
            var sqlStrings = await FindSql(solution);
            return this;
        }
        private static VisualStudioInstance SelectVisualStudioInstance(VisualStudioInstance[] visualStudioInstances)
        {
            Console.WriteLine("Multiple installs of MSBuild detected please select one:");
            for (int i = 0; i < visualStudioInstances.Length; i++)
            {
                Console.WriteLine($"Instance {i + 1}");
                Console.WriteLine($"    Name: {visualStudioInstances[i].Name}");
                Console.WriteLine($"    Version: {visualStudioInstances[i].Version}");
                Console.WriteLine($"    MSBuild Path: {visualStudioInstances[i].MSBuildPath}");
            }

            while (true)
            {
                var userResponse = Console.ReadLine();
                if (int.TryParse(userResponse, out int instanceNumber) &&
                    instanceNumber > 0 &&
                    instanceNumber <= visualStudioInstances.Length)
                {
                    return visualStudioInstances[instanceNumber - 1];
                }
                Console.WriteLine("Input not accepted, try again.");
            }
        }
    }
}
