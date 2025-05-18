# ETWWebService

## Overview

**ETWWebService** is a .NET Framework 4.7.2 application that runs a local HTTP server to stream real-time ETW (Event Tracing for Windows) events from registered ETW providers. It uses ETW manifests from the operating system to provide schema information for event data, and streams event details to any HTTP client (such as a web browser) in real time.

## Features

- Streams live ETW events from any provider with a registered manifest.
- Displays event data in a human-readable HTML format.
- Supports querying by ETW provider GUID.
- No .etl file processing; only live event streaming.
- Designed for local use (localhost only).

## Getting Started

1. Build the solution in Visual Studio (target: .NET Framework 4.7.2).
2. Run the application. It will start listening on:  
   `http://localhost:5000/`
3. Open a web browser and navigate to:  
   `http://localhost:5000/?<ProviderGUID>`  
   Example:  
   `http://localhost:5000/?30336ED4-E327-447C-9DE0-51B652C86108`
4. To find available ETW providers, run:  
   `logman.exe query providers`

## Usage

- The server streams ETW events as they occur.
- Each event is displayed as a formatted HTML paragraph.
- To stop streaming, close the browser tab or stop the application.

## Project Structure

- `Program.cs`: Main entry point and HTTP server logic.
- `EtwManifestUserDataReader.cs`: Reads ETW manifest schemas and parses event data.
- `EtwUserDataSchema.cs`: Represents the schema for ETW provider events.
- `UserDataColumn.cs`: Describes columns in event user data.
- `PropertyFlags.cs`, `UserDataColumnStatus.cs`: Supporting types.
- `test\ETWWebServiceTests`: Unit tests for core functionality.

## Requirements

- Windows OS with ETW support.
- .NET Framework 4.7.2.
- Administrative privileges may be required to access some ETW providers.

## Notes

- This tool is intended for development and diagnostics on local machines.
- It does not support remote access or authentication.
- The application does not persist or log event data.

## License

Copyright (c) Wayne Walter Berry. All rights reserved.
See LICENSE file for details (if available).
