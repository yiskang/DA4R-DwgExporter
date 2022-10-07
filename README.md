# Export DWG from Revit files with Design Automation

[![Design-Automation](https://img.shields.io/badge/Design%20Automation-v3-green.svg)](http://developer.autodesk.com/)

![Windows](https://img.shields.io/badge/Plugins-Windows-lightgrey.svg)
![.NET](https://img.shields.io/badge/.NET%20Framework-4.8-blue.svg)
[![Revit-2021](https://img.shields.io/badge/Revit-2021-lightgrey.svg)](http://autodesk.com/revit)


## Forge DA Setup

### Activity via [POST activities](https://forge.autodesk.com/en/docs/design-automation/v3/reference/http/activities-POST/)

```json
{
    "commandLine": [
        "$(engine.path)\\\\revitcoreconsole.exe /i \"$(args[inputFile].path)\" /al \"$(appbundles[DwgExporter].path)\""
    ],
    "parameters": {
        "inputFile": {
            "verb": "get",
            "description": "Input Revit File",
            "required": true,
            "localName": "$(inputFile)"
        },
        "inputJson": {
            "verb": "get",
            "description": "input Json parameters",
            "localName": "params.json"
        },
        "outputDwg": {
            "zip": true,
            "verb": "get",
            "description": "Exported DWG files",
            "localName": "exportedDwgs"
        }
    },
    "id": "yoursalias.DwgExporterActivity+dev",
    "engine": "Autodesk.Revit+2021",
    "appbundles": [
        "yoursalias.DwgExporter+dev"
    ],
    "settings": {},
    "description": "Activity of exporting DWG from RVT",
    "version": 1
}
```

### Workitem via [POST workitems](https://forge.autodesk.com/en/docs/design-automation/v3/reference/http/workitems-POST/)

```json
{
    "activityId": "yoursalias.DwgExporterActivity+dev",
    "arguments": {
   "inputFile": {
     "verb": "get",
     "url": "https://developer.api.autodesk.com/oss/v2/signedresources/...?region=US"
   },
   "inputJson": {
     "verb": "get",
     "url": "data:application/json,{ 'exportSettingName': 'my-dwg-export', 'viewSetName': 'CAD Export' }"
   },
   "outputDwg": {
     "verb": "put",
     "url": "https://developer.api.autodesk.com/oss/v2/signedresources/...?region=US"
   }
 }
}
```

## License

This sample is licensed under the terms of the [MIT License](http://opensource.org/licenses/MIT). Please see the [LICENSE](LICENSE) file for full details.

## Written by

Eason Kang [@yiskang](https://twitter.com/yiskang), [Forge Partner Development](http://forge.autodesk.com)