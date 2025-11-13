# Base64 Converter App - Blazor

A simple web application for converting files to base64 strings and base64 strings back to files. Built with Blazor Server and C#.

## Features

- Convert files to base64 strings
- Convert base64 strings back to files
- Download converted files
- Copy base64 strings to clipboard
- JSON to Files: Automatically detect base64 strings in JSON and convert them to files
- Modern, responsive UI with custom CSS styling
- Server-side processing with Blazor Server

## Project Structure

```
.
├── Pages/              # Blazor pages
│   ├── _Host.cshtml    # Host page
│   ├── _Layout.cshtml  # Layout template
│   └── Index.razor     # Main page component
├── Shared/             # Shared components
│   └── MainLayout.razor
├── Services/           # Business logic services
│   └── Base64ConverterService.cs
├── wwwroot/            # Static files
│   ├── css/
│   │   └── app.css
│   └── js/
│       └── download.js
├── App.razor           # App root component
├── Program.cs          # Application entry point
└── Base64AppBlazor.csproj
```

## Prerequisites

- .NET 8.0 SDK or higher
- Visual Studio 2022, Visual Studio Code, or any IDE with .NET support

## Setup Instructions

1. Navigate to the project directory:
```bash
cd "Base64 App Blazor"
```

2. Restore NuGet packages:
```bash
dotnet restore
```

3. Build the project:
```bash
dotnet build
```

4. Run the application:
```bash
dotnet run
```

The application will start and be available at `https://localhost:5001` or `http://localhost:5000` (depending on your configuration).

## Usage

1. Start the application using `dotnet run`
2. Open your browser and navigate to the URL shown in the console
3. To convert a file to base64:
   - Click "Select File" and choose a file
   - Click "Convert to Base64"
   - The base64 string will appear in the text area
4. To convert base64 to a file:
   - Paste a base64 string in the text area
   - Click "Convert to File"
   - Download the converted file
5. To extract files from JSON:
   - Upload a JSON file or paste JSON content
   - Click "Detect Base64 & Convert to Files"
   - Download any detected files

## Technical Details

- **Framework**: Blazor Server (.NET 8.0)
- **Language**: C#
- **Architecture**: Server-side rendering with SignalR for real-time updates
- **File Size Limit**: 50MB maximum
- **Supported File Types**: All file types (automatic detection for images, PDFs, ZIP files)

## API/Service Methods

The `Base64ConverterService` provides the following methods:

- `FileToBase64Async(Stream, string)` - Convert a file stream to base64
- `Base64ToFile(string, string?)` - Convert base64 string to file data
- `IsValidBase64(string)` - Validate if a string is valid base64
- `FindBase64InJson(string)` - Find and extract base64 strings from JSON

## Notes

- Maximum file size: 50MB
- The app supports all file types
- File type detection is automatic for common formats (images, PDFs, ZIP files)
- All processing happens server-side for security and performance

## Differences from React/Flask Version

- **Backend**: Single Blazor Server application (no separate backend API)
- **Frontend**: Razor components instead of React components
- **Language**: C# instead of JavaScript/Python
- **Architecture**: Server-side rendering with SignalR instead of REST API calls
- **Styling**: Custom CSS with Tailwind-inspired classes instead of Tailwind CSS

