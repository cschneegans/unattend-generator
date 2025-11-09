# UnattendGenerator ✨

Welcome to **UnattendGenerator**! This project is a .NET library that empowers you to create `autounattend.xml` files for seamless, automated Windows installations. 💻🚀

Whether you're a system administrator, a developer, or a tech enthusiast, UnattendGenerator is designed to make your life easier by automating the Windows setup process.

## 🌟 Features

- **Easy Configuration**: A simple and intuitive API allows you to customize your Windows installation with ease.
- **Extensive Automation**: Automate everything from language and keyboard settings to user accounts and bloatware removal.
- **Winget Integration**: Automatically install your favorite applications using [Winget](https://docs.microsoft.com/en-us/windows/package-manager/winget/), the official Windows Package Manager.
- **Modular and Extensible**: The project is designed with a modular architecture, making it easy to extend and add new features.

## 🚀 Getting Started

To get started, you can use the `UnattendGenerator` as a standalone application or integrate it into your own .NET projects.

### Prerequisites

- .NET 8 SDK (or later)
- Visual Studio 2022 (or your favorite C# editor)

### Usage

1.  **Clone the repository:**

    ```bash
    git clone https://github.com/cschneegans/unattend-generator.git
    cd unattend-generator
    ```

2.  **Open the project in Visual Studio:**

    Change the project's output type from 'Class Library' to 'Console Application' in the project properties.

3.  **Customize your configuration:**

    Open the `Example.cs` file and modify the `Configuration.Default` object to suit your needs. Here's an example that sets the language to US English, removes Teams and Outlook, and installs PowerToys and 7-Zip using Winget:

    ```csharp
    UnattendGenerator generator = new();
    XmlDocument xml = generator.GenerateXml(
      Configuration.Default with
      {
        LanguageSettings = new UnattendedLanguageSettings(
          ImageLanguage: generator.Lookup<ImageLanguage>("en-US"),
          LocaleAndKeyboard: new LocaleAndKeyboard(
            generator.Lookup<UserLocale>("en-US"),
            generator.Lookup<KeyboardIdentifier>("00000409")
          ),
          LocaleAndKeyboard2: null,
          LocaleAndKeyboard3: null,
          GeoLocation: generator.Lookup<GeoLocation>("244")
        ),
        Bloatwares =
        [
          .. generator.Bloatwares.Values.Where(b => b.Id is "RemoveTeams" or "RemoveOutlook"),
        ],
        Winget = new WingetSettings(
          Packages:
          [
            "Microsoft.PowerToys",
            "7zip.7zip"
          ]
        ),
      }
    );
    ```

4.  **Run the application:**

    Press `F5` in Visual Studio to run the `Example.cs` and generate your `autounattend.xml` file. The file will be saved to your temporary directory (`%TEMP%`).

## 🤝 Contributing

Contributions are welcome! If you have any ideas, suggestions, or bug reports, please open an issue or submit a pull request. Let's make UnattendGenerator even better together! ❤️

## 📄 License

This project is licensed under the MIT License. See the [LICENSE.txt](LICENSE.txt) file for details.
