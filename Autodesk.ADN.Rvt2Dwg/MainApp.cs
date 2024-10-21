// (C) Copyright 2024 by Autodesk, Inc. 
//
// Permission to use, copy, modify, and distribute this software
// in object code form for any purpose and without fee is hereby
// granted, provided that the above copyright notice appears in
// all copies and that both that copyright notice and the limited
// warranty and restricted rights notice below appear in all
// supporting documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS. 
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE.  AUTODESK,
// INC. DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL
// BE UNINTERRUPTED OR ERROR FREE.
//
// Use, duplication, or disclosure by the U.S. Government is
// subject to restrictions set forth in FAR 52.227-19 (Commercial
// Computer Software - Restricted Rights) and DFAR 252.227-7013(c)
// (1)(ii)(Rights in Technical Data and Computer Software), as
// applicable.
//

using System;
using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using DesignAutomationFramework;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Autodesk.ADN.Rvt2Dwg
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MainApp : IExternalDBApplication
    {
        public ExternalDBApplicationResult OnStartup(ControlledApplication application)
        {
            // Hook up the CustomFailureHandling failure processor.
            Application.RegisterFailuresProcessor(new CustomFailuresProcessor());

            DesignAutomationBridge.DesignAutomationReadyEvent += HandleDesignAutomationReadyEvent;
            return ExternalDBApplicationResult.Succeeded;
        }

        public ExternalDBApplicationResult OnShutdown(ControlledApplication application)
        {
            return ExternalDBApplicationResult.Succeeded;
        }

        private void HandleDesignAutomationReadyEvent(object sender, DesignAutomationReadyEventArgs e)
        {
            LogTrace("Design Automation Ready event triggered...");

            // Hook up the CustomFailureHandling failure processor.
            Application.RegisterFailuresProcessor(new CustomFailuresProcessor());

            e.Succeeded = true;
            e.Succeeded = this.DoTask(e.DesignAutomationData);
        }
        private bool DoTask(DesignAutomationData data)
        {
            if (data == null)
                return false;

            Application app = data.RevitApp;
            if (app == null)
            {
                LogTrace("Error occurred");
                LogTrace("Invalid Revit App.");
                return false;
            }

            string modelPath = data.FilePath;
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                LogTrace("Error occurred");
                LogTrace("Invalid File Path.");
                return false;
            }

            var doc = data.RevitDoc;
            if (doc == null)
            {
                LogTrace("Error occurred");
                LogTrace("Invalid Revit DB Document.");
                return false;
            }

            var inputParams = JsonConvert.DeserializeObject<InputParams>(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "params.json")));
            if (inputParams == null)
            {
                LogTrace("Invalid Input Params or Empty JSON Inpu.t");
                return false;
            }

            LogTrace("Creating export folder...");

            var exportPath = Path.Combine(Directory.GetCurrentDirectory(), "exportedDwgs");
            if (!Directory.Exists(exportPath))
            {
                try
                {
                    Directory.CreateDirectory(exportPath);
                }
                catch (Exception ex)
                {
                    this.PrintError(ex);
                    return false;
                }
            }

            LogTrace(string.Format("Export Path: `{0}`...", exportPath));

            LogTrace("Collecting views...");
            var viewIds = new List<ElementId>();

            if (inputParams.ExportAllViews == true)
            {
                LogTrace("- Collecting all views...");
                try
                {
                    var viewElemIds = new List<ElementId>();
                    IEnumerable<View> views = new FilteredElementCollector(doc)
                                                .WhereElementIsNotElementType()
                                                .OfCategory(BuiltInCategory.OST_Views)
                                                .Cast<View>()
                                                .Where(view => (!view.IsTemplate) && (view.CanBePrinted));

                    foreach (View view in views)
                    {
                        if ((view.ViewType == ViewType.Rendering) && (inputParams.IncludingRenderingViews == false))
                            continue;

                        viewElemIds.Add(view.Id);
                    }

                    viewIds.AddRange(viewElemIds);
                }
                catch (Exception ex)
                {
                    this.PrintError(ex);
                    return false;
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(inputParams.ViewSetName))
                {
                    LogTrace(string.Format("- From given view set name `{0}`...", inputParams.ViewSetName));
                    using (var collector = new FilteredElementCollector(doc))
                    {
                        var viewSheetSet = collector
                                            .OfClass(typeof(ViewSheetSet))
                                            .Cast<ViewSheetSet>()
                                            .Where(viewSet => viewSet.Name == inputParams.ViewSetName)
                                            .FirstOrDefault();

                        if (viewSheetSet == null || viewSheetSet.Views.Size <= 0)
                            throw new InvalidDataException("Invalid input `viewSetName`, no view set with the given name found or no views found in the view set!");

                        var viewElemIds = new List<ElementId>();
                        foreach (View view in viewSheetSet.Views)
                        {
                            if (!view.CanBePrinted)
                            {
                                LogTrace(string.Format("Warning: view `{0}` cannot be exported.", view.UniqueId));
                                continue;
                            }

                            viewElemIds.Add(view.Id);
                        }

                        viewIds.AddRange(viewElemIds);
                    }
                }

                if (inputParams.ViewIds != null && inputParams.ViewIds.Count > 0)
                {
                    LogTrace("- From given view Ids...");
                    try
                    {
                        var viewElemIds = new List<ElementId>();
                        foreach (var viewGuid in inputParams.ViewIds)
                        {
                            var view = doc.GetElement(viewGuid) as View;
                            if (view == null || (!view.CanBePrinted))
                            {
                                LogTrace(string.Format("Warning: No view found with gieven unqique id `{0}` or view cannot be exported.", viewGuid));
                                continue;
                            }

                            viewElemIds.Add(view.Id);
                        }

                        viewIds.AddRange(viewElemIds);
                    }
                    catch (Exception ex)
                    {
                        this.PrintError(ex);
                        return false;
                    }
                }
            }

            if (viewIds == null || viewIds.Count() <= 0)
            {
                LogTrace("Error occurred");
                LogTrace("No views to be exported...");
                return false;
            }

            LogTrace("Starting the export task...");

            using (var transGroup = new TransactionGroup(doc, "Starts exporting DWG"))
            {
                try
                {
                    transGroup.Start();

                    DWGExportOptions exportOpts = GetDWGExportOptions(doc, inputParams);
                    if (exportOpts != null && inputParams.ApplyCustomSettings == true && inputParams.CustomSettings != null)
                    {
                        ApplyCustomDWGExportOptions(doc, exportOpts, inputParams.CustomSettings, modelPath);
                    }

                    if (exportOpts != null && exportOpts.ExportOfSolids != SolidGeometry.ACIS)
                    {
                        using (var trans = new Transaction(doc, "Ensure ExportDWGSettings use ASCI Solid presentation."))
                        {
                            trans.Start();
                            exportOpts.ExportOfSolids = SolidGeometry.ACIS;
                            trans.Commit();
                        }
                    }

                    var rvt_filename = Path.GetFileNameWithoutExtension(modelPath);

                    foreach (var viewId in viewIds)
                    {
                        var view = doc.GetElement(viewId) as View;
                        if (view is View3D)
                        {
                            using (var trans = new Transaction(doc, "Ensure 3D view uses the Shaded display style."))
                            {
                                trans.Start();
                                view.DisplayStyle = DisplayStyle.Shading;
                                trans.Commit();
                            }
                        }

                        var viewType = doc.GetElement(view.GetTypeId());
                        var name = string.Format("{0}-{1} - {2}", rvt_filename, viewType.Name, view.Name);
                        name = name.Replace("{", "_").Replace("}", "_").ReplaceInvalidFileNameChars();
                        var filename = string.Format("{0}.dwg", name);

                        LogTrace(string.Format("Exporting `{0}`...", filename));

                        ElementId[] ids = { view.Id };

                        doc.Export(exportPath, filename, ids, exportOpts);
                    }
                }
                catch (Autodesk.Revit.Exceptions.InvalidPathArgumentException ex)
                {
                    this.PrintError(ex);
                    return false;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    this.PrintError(ex);
                    return false;
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    this.PrintError(ex);
                    return false;
                }
                catch (Exception ex)
                {
                    this.PrintError(ex);
                    return false;
                }

                transGroup.RollBack();
            }

            LogTrace("Exporting completed...");


            return true;
        }

        private DWGExportOptions GetDWGExportOptions(Document document, InputParams inputParams)
        {
            LogTrace("Getting Export DWG settings.");

            ExportDWGSettings settings = null;

            if (!string.IsNullOrWhiteSpace(inputParams.ExportSettingName))
            {
                LogTrace(string.Format("- Getting Export DWG settings by given export setting name `{0}`.", inputParams.ExportSettingName));

                settings = ExportDWGSettings.FindByName(document, inputParams.ExportSettingName);

                if (settings == null)
                    LogTrace(string.Format("- Warning: No export DWG settings found with given export setting name `{0}`.", inputParams.ExportSettingName));
            }
            else
            {
                LogTrace("- Getting Export DWG settings since no export setting name specified.");

                settings = ExportDWGSettings.GetActivePredefinedSettings(document);

                if (settings == null)
                    LogTrace("- Warning: No active predefined settings found for exporting DWG in the Revit doucment.");
            }

            if (settings == null)
            {
                LogTrace("- Creating an ExportDWGSettings with default values.");
                using (var trans = new Transaction(document, "Create the ExportDWGSettings with defaults"))
                {
                    trans.Start();
                    settings = ExportDWGSettings.Create(document, "Export DWG Default");
                    trans.Commit();
                }
                LogTrace("- Default `ExportDWGSettings` created.");
            }

            LogTrace(string.Format("Export DWG using settings `{0}`.", settings.Name));

            return settings?.GetDWGExportOptions();

        }

        private DWGExportOptions ApplyCustomDWGExportOptions(Document document, DWGExportOptions exportOptions, CustomSettings userSettings, string filePath)
        {
            LogTrace("Applying custom export settings on the fly.");
            LogTrace("- Applying custom settings to the `DWGExportOptions`.");

            using (var trans = new Transaction(document, "Apply custom settings to the DWGExportOptions"))
            {
                trans.Start();

                if (userSettings.TargetUnit.HasValue)
                    exportOptions.TargetUnit = userSettings.TargetUnit.Value;

                if (userSettings.UseSharedCoords.HasValue)
                    exportOptions.SharedCoords = userSettings.UseSharedCoords.Value;

                if (userSettings.SolidMode.HasValue)
                    exportOptions.ExportOfSolids = userSettings.SolidMode.Value;

                //if (!string.IsNullOrWhiteSpace(userSettings.LayerMapping))
                //{
                //    exportOptions.LayerMapping = userSettings.LayerMapping;

                //    if (!string.IsNullOrWhiteSpace(userSettings.OverrideLayerFilename))
                //    {
                //        var layerMapFilename = Path.Combine(Path.GetDirectoryName(filePath), userSettings.OverrideLayerFilename);
                //        if (File.Exists(layerMapFilename))
                //            exportOptions.LayerMapping = layerMapFilename;
                //        else
                //            throw new InvalidDataException("Specified layer mapping file not found");
                //    }
                //}

                //if (userSettings.LineScaling.HasValue)
                //    exportOptions.LineScaling = userSettings.LineScaling.Value;

                //if(!string.IsNullOrWhiteSpace(userSettings.OverrideLineTypesFileName))
                //{
                //    var lineTypesFilename = Path.Combine(Path.GetDirectoryName(filePath), userSettings.OverrideLineTypesFileName);
                //    if (File.Exists(lineTypesFilename))
                //        exportOptions.LinetypesFileName = lineTypesFilename;
                //    else
                //        throw new InvalidDataException("Specified line types file not found");
                //}

                //if (userSettings.UseHatchBackgroundColor.HasValue)
                //{
                //    exportOptions.UseHatchBackgroundColor = userSettings.UseHatchBackgroundColor.Value;
                //    if (userSettings.HatchBackgroundColor != null && userSettings.HatchBackgroundColor.IsValid)
                //        exportOptions.HatchBackgroundColor = userSettings.HatchBackgroundColor;
                //}

                //if (!string.IsNullOrWhiteSpace(userSettings.OverrideHatchPatternsFilename))
                //{
                //    var hatchPatternsFilename = Path.Combine(Path.GetDirectoryName(filePath), userSettings.OverrideHatchPatternsFilename);
                //    if (File.Exists(hatchPatternsFilename))
                //        exportOptions.HatchPatternsFileName = hatchPatternsFilename;
                //    else
                //        throw new InvalidDataException("Specified hatch patterns file not found");
                //}

                //if (userSettings.ColorMode.HasValue)
                //    exportOptions.Colors = userSettings.ColorMode.Value;

                //if (userSettings.TextTreatment.HasValue)
                //    exportOptions.TextTreatment = userSettings.TextTreatment.Value;

                //if (userSettings.ExportingRoomsAndArea.HasValue)
                //    exportOptions.ExportingAreas = userSettings.ExportingRoomsAndArea.Value;

                //if (userSettings.MarkNonplotLayers.HasValue)
                //{
                //    exportOptions.MarkNonplotLayers = userSettings.MarkNonplotLayers.Value;
                //    if (!string.IsNullOrWhiteSpace(userSettings.NonplotSuffix))
                //        exportOptions.NonplotSuffix = userSettings.NonplotSuffix;
                //}

                //if (userSettings.HideScopeBox.HasValue)
                //    exportOptions.HideScopeBox = userSettings.HideScopeBox.Value;

                //if (userSettings.HideReferencePlane.HasValue)
                //    exportOptions.HideReferencePlane = userSettings.HideReferencePlane.Value;

                //if (userSettings.HideUnreferenceViewTags.HasValue)
                //    exportOptions.HideUnreferenceViewTags = userSettings.HideUnreferenceViewTags.Value;

                //if (userSettings.PreserveCoincidentLines.HasValue)
                //    exportOptions.PreserveCoincidentLines = userSettings.PreserveCoincidentLines.Value;

                //if (userSettings.ExportingViewsOnSheetSeparately.HasValue)
                //    exportOptions.MergedViews = !userSettings.ExportingViewsOnSheetSeparately.Value;

                trans.Commit();
            }

            LogTrace("- Custom settings to the `DWGExportOptions` applied.");

            return exportOptions;
        }

        private void PrintError(Exception ex)
        {
            LogTrace("Error occurred");
            LogTrace(ex.Message);

            if (ex.InnerException != null)
                LogTrace(ex.InnerException.Message);
        }

        /// <summary>
        /// This will appear on the Design Automation output
        /// </summary>
        public static void LogTrace(string format, params object[] args)
        {
#if DEBUG
            System.Diagnostics.Trace.WriteLine(string.Format(format, args));
#endif
            System.Console.WriteLine(format, args);
        }
    }
}
