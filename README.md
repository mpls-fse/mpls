# Multi-Phase Local Search

Multi-Phase Local Search (MPLS) is an algorithm to optimize container reallocation. This repository contains the source code for the implementation of the algorithm.

## Source Structure

The source code are in `src` folder.

* `./src/Solver`: the implementation of [MPLS](./src/Solver/MplsAllocationSolver.cs);
* `./src/Model`: model classes for container reallocation scenario;
* `./src/SolverContract`: model classes for the interface of container reallocation solver;
* `./src/Env`: sample case for invoking the solver;
* `./src/Utils`: helpers and utilities functions;

## How to Run the Project

Prerequisite: The project requires [.Net](https://dotnet.microsoft.com/download) environment.

```bash
cd ./src/
dotnet run
```
