# Cloud Permission Preflight Notes

Autodesk Support states that a user needs a valid Forma Design Collaboration/BIM Collaborate Pro entitlement and `View + Download + Upload + Edit` folder permissions to open or collaborate on a Revit cloud-workshared model. A user can therefore enumerate or view an ACC item but still fail when Revit attempts to open it.

The ACC/BIM 360 folder permissions endpoint is:

`GET https://developer.api.autodesk.com/bim360/docs/v1/projects/{projectId}/folders/{folderId}/permissions`

The endpoint returns permissions for users, companies, and roles. Autodesk documents that the effective permission set is the union of `actions` and `inheritActions`, including permissions granted through roles or companies. The API documentation requires `data:read`; access may depend on whether the caller has suitable account or project visibility.

Implementation implication: retain the existing Revit-side exception handling as the authoritative enforcement point, skip retries for an unauthorized result, and add an optional best-effort APS permission preflight only when a usable folder identifier and suitable token scopes are available. A failed or unavailable permission preflight must not be treated as proof of denial because Revit entitlement is enforced separately.

Sources:
- https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/Error-You-do-not-have-permission-to-perform-this-action-appears-when-attempting-to-open-and-collaborate-on-a-BIM-360-file-from-Revit.html
- https://aps.autodesk.com/en/docs/bim360/v1/tutorials/document-management/retrieve-user-permissions
- https://aps.autodesk.com/en/docs/acc/v1/reference/http/document-management-projects-project_id-folders-folder_id-permissions-GET
