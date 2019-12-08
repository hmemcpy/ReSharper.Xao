﻿using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Navigation;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.Util;

namespace ReSharper.Xao
{
  [RelatedFilesProvider(typeof(KnownProjectFileType))]
  public class ViewModelRelatedFilesProvider : IRelatedFilesProvider
  {
    private static readonly string[] ViewSuffixes = {"View", "Flyout", "UserControl", "Page"};

    public IEnumerable<RelatedFileOccurence> GetRelatedFiles(IProjectFile projectFile)
    {
      var typeNamesInFile = GetTypeNamesDefinedInFile(projectFile).ToList();

      // Look for the candidate types in the solution.
      var solution = projectFile.GetSolution();
      var candidateTypes = new LocalList<IClrDeclaredElement>();
      foreach (var candidateTypeName in GetTypeCandidates(typeNamesInFile))
      {
        var types = FindTypesByShortName(solution, candidateTypeName);
        candidateTypes.AddRange(types);
      }

      // Get the source files for each of the candidate types.
      var sourceFiles = new LocalList<IPsiSourceFile>();
      foreach (var type in candidateTypes)
      {
        var sourceFilesForCandidateType = type.GetSourceFiles();
        sourceFiles.AddRange(sourceFilesForCandidateType.ResultingList());
      }

      var elementCollector = new RecursiveElementCollector<ITypeDeclaration>();
      foreach (var psiSourceFile in sourceFiles)
      foreach (var file in psiSourceFile.EnumerateDominantPsiFiles())
      {
        elementCollector.ProcessElement(file);
      }

      var elements = elementCollector.GetResults();
      var projectFiles = elements.Select(declaration => declaration.GetSourceFile().ToProjectFile());

      var thisProjectName = projectFile.GetProject()?.Name;

      var occurences = new LocalList<RelatedFileOccurence>();

      foreach (var file in projectFiles.OfType<ProjectFileImpl>().Distinct(x => x.Location.FullPath))
      {
        // Remove all extensions (e.g.: .xaml.cs).
        var fileName = file.Name;
        var dotPos = fileName.IndexOf('.');
        if (dotPos != -1)
        {
          fileName = fileName.Substring(0, dotPos);
        }

        var relationKind = fileName.EndsWith("ViewModel") ? "ViewModel" : "View";

        var projectName = file.GetProject()?.Name;

        if (projectName != null &&
            !string.Equals(thisProjectName, projectName, StringComparison.OrdinalIgnoreCase))
        {
          relationKind += $" (in {projectName})";
        }

        occurences.Add(new RelatedFileOccurence(file, relationKind, projectFile));
      }

      return occurences.ReadOnlyList();
    }

    [NotNull, Pure]
    private static IEnumerable<string> GetTypeCandidates([NotNull] IEnumerable<string> typeNamesInFile)
    {
      var candidates = new LocalList<string>();

      // For each type name in the file, create a list of candidates.
      foreach (var typeName in typeNamesInFile)
      {
        // If a view model...
        if (typeName.EndsWith("ViewModel", StringComparison.OrdinalIgnoreCase))
        {
          // Remove ViewModel from end and add all the possible suffixes.
          var baseName = typeName.Substring(0, typeName.Length - 9);
          candidates.AddRange(ViewSuffixes.Select(suffix => baseName + suffix));

          // Add base if it ends in one of the view suffixes.
          if (ViewSuffixes.Any(suffix => baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
          {
            candidates.Add(baseName);
          }
        }

        foreach (var suffix in ViewSuffixes)
        {
          if (typeName.EndsWith(suffix))
          {
            // Remove suffix and add ViewModel.
            var baseName = typeName.Substring(0, typeName.Length - suffix.Length);
            var candidate = baseName + "ViewModel";
            candidates.Add(candidate);

            // Just add ViewModel
            candidate = typeName + "ViewModel";
            candidates.Add(candidate);
          }
        }
      }

      return candidates.ReadOnlyList();
    }

    [NotNull, Pure]
    private static IEnumerable<string> GetTypeNamesDefinedInFile([NotNull] IProjectFile projectFile)
    {
      var psiSourceFile = projectFile.ToSourceFile();
      if (psiSourceFile == null) return EmptyList<string>.InstanceList;

      var symbolCache = psiSourceFile.GetPsiServices().Symbols;

      return symbolCache.GetTypesAndNamespacesInFile(psiSourceFile)
        .OfType<ITypeElement>()
        .Select(element => element.ShortName);
    }

    [NotNull, Pure]
    private static List<IClrDeclaredElement> FindTypesByShortName([NotNull] ISolution solution, [NotNull] string shortTypeName)
    {
      var symbolCache = solution.GetPsiServices().Symbols;
      var symbolScope = symbolCache.GetSymbolScope(LibrarySymbolScope.FULL, caseSensitive: false);

      return symbolScope.GetElementsByShortName(shortTypeName).ToList();
    }
  }
}