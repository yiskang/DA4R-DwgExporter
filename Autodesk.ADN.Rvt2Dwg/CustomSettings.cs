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

using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Autodesk.ADN.Rvt2Dwg
{
    class CustomSettings
    {
        [JsonProperty("targetUnit")]
        [JsonConverter(typeof(StringEnumConverter))]
        public ExportUnit? TargetUnit { get; set; }

        public bool? UseSharedCoords { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public SolidGeometry? SolidMode { get; set; }

        public string LayerMapping { get; set; }

        public string OverrideLayerFilename { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public LineScaling? LineScaling { get; set; }

        public string OverrideLineTypesFileName { get; set; }

        public bool? UseHatchBackgroundColor { get; set; }

        public Color HatchBackgroundColor { get; set; }

        public string OverrideHatchPatternsFilename { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public ExportColorMode? ColorMode { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public TextTreatment? TextTreatment { get; set; }

        public bool? ExportingRoomsAndArea { get; set; }

        public bool? MarkNonplotLayers { get; set; }

        public string NonplotSuffix { get; set; }

        public bool? HideScopeBox { get; set; }
        
        public bool? HideReferencePlane { get; set; }
        
        public bool? HideUnreferenceViewTags { get; set; }
        
        public bool? PreserveCoincidentLines { get; set; }

        public bool? ExportingViewsOnSheetSeparately { get; set; }
    }
}
