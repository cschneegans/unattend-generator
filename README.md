# UnattendGenerator ✨

Welcome to **UnattendGenerator**! This project is a .NET library that empowers you to create `autounattend.xml` files for seamless, automated Windows installations. 💻🚀

Whether you're a system administrator, a developer, or a tech enthusiast, UnattendGenerator is designed to make your life easier by automating the Windows setup process.

## 🌟 Features

- **Easy Configuration**: A simple and intuitive API allows you to customize your Windows installation with ease.
- **Extensive Automation**: Automate everything from language and keyboard settings to user accounts and bloatware removal.
- **Winget Integration**: Automatically install your favorite applications using [Winget](https://docs.microsoft.com/en-us/windows/package-manager/winget/), the official Windows Package Manager.
- **Modular and Extensible**: The project is designed with a modular architecture, making it easy to extend and add new features.

## 🚀 Getting Started

This project is now a graphical application! 🖼️

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

    Open the `UnattendGenerator.csproj` file in Visual Studio.

3.  **Run the application:**

    Press `F5` to build and run the application.

4.  **Configure your installation:**

    -   Use the **Winget** tab to add or remove programs to be installed automatically. The application will verify that the package exists.
    -   Use the **Settings** tab to customize all other aspects of your Windows installation.

5.  **Generate the `autounattend.xml` file:**

    Click the "Générer le fichier autounattend.xml..." button to save the file to your desired location, for example, a USB drive.

## 🤝 Contributing

Contributions are welcome! If you have any ideas, suggestions, or bug reports, please open an issue or submit a pull request. Let's make UnattendGenerator even better together! ❤️

## 📄 License

This project is licensed under the MIT License. See the [LICENSE.txt](LICENSE.txt) file for details.
