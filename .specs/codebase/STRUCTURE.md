# Structure

The repository follows the same high-level pattern used by the neighboring RentifyX services:

- `src` for runnable code and shared libraries
- `tests` for unit and integration coverage
- `iac` for Terraform and deployment assets
- `docs` for ADRs and design notes
- `.specs` for workflow traceability

The current repository state includes the initial solution scaffold, shared library, and function project skeletons, all tied together through the root solution file.
