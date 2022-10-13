// (C) Copyright 2022 by Autodesk, Inc. 
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
                LogTrace("Error occured");
                LogTrace("Invalid Revit App.");
                return false;
            }

            string modelPath = data.FilePath;
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                LogTrace("Error occured");
                LogTrace("Invalid File Path.");
                return false;
            }

            var doc = data.RevitDoc;
            if (doc == null)
            {
                LogTrace("Error occured");
                LogTrace("Invalid Revit DB Document.");
                return false;
            }

            var inputParams = JsonConvert.DeserializeObject<InputParams>(File.ReadAllText("params.json"));
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
            IEnumerable<ElementId> viewIds = null;

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
                        viewElemIds.Add(view.Id);
                    }

                    viewIds = viewElemIds;
                }
            }
            else
            {
                LogTrace("- From given view Ids...");
                try
                {
                    if (inputParams.ViewIds == null || inputParams.ViewIds.Count() <= 0)
                    {
                        throw new InvalidDataException("Invalid input` viewIds` while the `viewSetName` value is not specified!");
                    }

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

                    viewIds = viewElemIds;
                }
                catch (Exception ex)
                {
                    this.PrintError(ex);
                    return false;
                }
            }

            if (viewIds == null || viewIds.Count() <= 0)
            {
                LogTrace("Error occured");
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
                    if (exportOpts != null && exportOpts.ExportOfSolids != SolidGeometry.ACIS)
                    {
                        using (var trans = new Transaction(doc, "Ensure ExportDWGSettings use ASCI Solid presentation."))
                        {
                            trans.Start();
                            exportOpts.ExportOfSolids = SolidGeometry.ACIS;
                            trans.Commit();
                        }
                    }

                    var rvt_filename = Path.GetFileNameWithoutExtension(doc.PathName);

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

            if (!string.IsNullOrWhiteSpace(inputParams.exportSettingName))
            {
                LogTrace(string.Format("- Getting Export DWG settings by given export setting name `{0}`.", inputParams.exportSettingName));

                settings = ExportDWGSettings.FindByName(document, inputParams.exportSettingName);

                if (settings == null)
                    LogTrace(string.Format("- Warning: No export DWG settings found with given export setting name `{0}`.", inputParams.exportSettingName));
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
                using (var trans = new Transaction(document, "Create the ExportDWGSettings with defaults."))
                {
                    trans.Start();
                    settings = ExportDWGSettings.Create(document, "Export DWG Default");
                    trans.Commit();
                }
                LogTrace("- Default ExportDWGSettings created.");
            }

            LogTrace(string.Format("Export DWG using settings `{0}`.", settings.Name));

            return settings?.GetDWGExportOptions();

        }

        private void PrintError(Exception ex)
        {
            LogTrace("Error occured");
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
