# xcord-topo

Topology designer module for [Xcord](https://github.com/xcord-net). A visual infrastructure editor and Terraform generator for planning and deploying Xcord infrastructure.

## Structure

```
xcord-topo/
├── src/
│   ├── backend/     # ASP.NET Core API (.NET 9)
│   └── frontend/    # Canvas editor SPA (SolidJS + Vite)
├── docker/          # Dev stack + production Dockerfile
└── run.sh           # Build + run script
```

## Backend

File-system-based persistence (no database). Features:

- Topology CRUD, validation, duplication
- Terraform HCL generation, cost estimation, execution with streaming output
- Provider registry (regions, compute plans)
- Migration planning (topology diffs, migration HCL)
- Deploy wizard (credential management, active deployments)

## Frontend

SolidJS canvas editor with drag-and-drop nodes, wire routing, undo/redo, and a deploy wizard. Nodes represent containers and images; wires represent network connections between ports.

## Running

```bash
# Production build + run
./run.sh

# Dev mode (backend in Docker, Vite dev server with HMR)
./run.sh dev
```

## Running Tests

```bash
# Backend tests
dotnet test src/backend/XcordTopo.sln --verbosity quiet

# Frontend tests
cd src/frontend && npx vitest run
```

## License

Apache 2.0 -- see [LICENSE](LICENSE).
