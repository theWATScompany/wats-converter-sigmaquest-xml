# SigmaQuest / SigmaSure XML Converter

Converts SigmaQuest (SigmaSure) XML test result files to WATS UUT reports.

## Integration Details

| Property | Value |
|----------|-------|
| **Category** | WATS Client converter |
| **Type** | FileConverter |
| **Format** | XML |
| **Test type** | General |

## About

SigmaQuest (now SigmaSure) is a quality management and test data analytics platform. This converter imports their XML export format into WATS.

## Getting Started

* [What is WATS?](https://wats.com)
* [Setting up a custom converter](https://support.wats.com/hc/en-us/articles/13344321749788-Setting-up-a-custom-converter)

## Download

The recommended installation method is via the installer. Download the latest release from the [Releases](https://github.com/Virinco/WATS-Converter-SigmaQuest-XML/releases/latest) page.

## Installation

### Using the Installer (Recommended)
1. Download the installer (`.exe`) from the [Releases](https://github.com/Virinco/WATS-Converter-SigmaQuest-XML/releases/latest) page.
2. Run the installer - it will automatically place the converter in the correct WATS Client folder.
3. Restart the WATS Client Service.

### Manual DLL Installation
1. Download the `.dll` file from the [Releases](https://github.com/Virinco/WATS-Converter-SigmaQuest-XML/releases/latest) page.
2. In the WATS Client Configurator, go to Converters, click Add, and browse for the downloaded DLL.
3. Select the appropriate converter class from the drop-down.

## Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `operationTypeCode` | 10 | The operation type code to use for imported reports. |

## Testing

The project uses the [MSTest framework](https://docs.microsoft.com/en-us/visualstudio/test/quick-start-test-driven-development-with-test-explorer) for testing.

See `ConverterTests.cs` for setup instructions and test configuration.

## Contributing

We welcome contributions! Feel free to open an issue or create a pull request.

## Troubleshooting

### Converter failed to start

- Ensure the WATS Client Service has folder permission to the input path.
- Restart the WATS Client Service after configuration changes.

### Converter class drop-down is empty

- The DLL file may be blocked by Windows. Right-click the file, open Properties, and click Unblock.

### Other issues

Contact [WATS Support](mailto:support@wats.com) and include the `wats.log` file.

## Resources
- [GitHub Repository](https://github.com/Virinco/WATS-Converter-SigmaQuest-XML)
- [WATS Documentation](https://support.wats.com)
- [Setting up a custom converter](https://support.wats.com/hc/en-us/articles/13344321749788-Setting-up-a-custom-converter)

## License

See [LICENSE](LICENSE.md) for details.


