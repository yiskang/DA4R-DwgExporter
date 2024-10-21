﻿// (C) Copyright 2024 by Autodesk, Inc. 
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

using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace Autodesk.ADN.Rvt2Dwg
{
    class CustomFailuresProcessor : IFailuresProcessor
    {
        public void Dismiss(Document document)
        {
        }

        public FailureProcessingResult ProcessFailures(FailuresAccessor data)
        {
            IList<FailureMessageAccessor> failures = data.GetFailureMessages();

            const int MAX_RESOLUTION_ATTEMPTS = 3;
            bool hasError = false;
            bool hasWarning = false;

            foreach (FailureMessageAccessor f in failures)
            {
                // check how many resolutions types were attempted to try to prevent
                // entering infinite loop
                var resolutionTypeList = data.GetAttemptedResolutionTypes(f);
                if (resolutionTypeList.Count >= MAX_RESOLUTION_ATTEMPTS)
                {
                    MainApp.LogTrace("Failure: Attempted to resolve the failure "
                      + f.GetDescriptionText() + " " + resolutionTypeList.Count
                      + " times with resolution " + f.GetCurrentResolutionType()
                      + ". Rolling back transaction.");
                    return FailureProcessingResult.ProceedWithRollBack;
                }

                FailureSeverity fseverity = data.GetSeverity();

                if (fseverity == FailureSeverity.Error)
                {
                    ICollection<ElementId> failingElementIds = f.GetFailingElementIds();

                    if (failingElementIds.Count > 0)
                    {
                        if (f.HasResolutionOfType(FailureResolutionType.DetachElements))
                        {
                            MainApp.LogTrace("FailureInstruction `Delete Element(s)` found. It will delete failling elements to resolve the failure.");
                            MainApp.LogTrace($"Following elements will be delted: {string.Join(",", failingElementIds.Select(eid => eid.IntegerValue))}");

                            f.SetCurrentResolutionType(FailureResolutionType.DeleteElements);
                        }

                        if (f.GetFailureDefinitionId() == BuiltInFailures.FamilyFailures.FamilyIsCorruptError)
                        {
                            data.ResolveFailure(f);
                        }

                        hasError = true;
                        data.ResolveFailure(f);
                    }
                }

                if (fseverity == FailureSeverity.Warning)
                {
                    hasWarning = true;
                    data.DeleteWarning(f);
                }


                if (hasWarning || hasError)
                {
                    return FailureProcessingResult.ProceedWithCommit;
                }
            }

            MainApp.LogTrace("Attempting to continue.");
            return FailureProcessingResult.Continue;
        }
    }
}
