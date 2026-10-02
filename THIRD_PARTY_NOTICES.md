# Third-party components

Prism is MIT-licensed. Product names remain their owners' trademarks; no endorsement is implied.

| Component | Use | License/source |
| --- | --- | --- |
| .NET 8 / WPF | Bundled in release ZIPs | MIT and component notices in `docs/legal/`; [runtime](https://github.com/dotnet/runtime), [WPF](https://github.com/dotnet/wpf) |
| Python | User-installed, not bundled | [PSF](https://docs.python.org/3/license.html) |
| pywinpty 3.0.5 | Windows pseudo-terminal, installed by Setup | [MIT](https://github.com/andfoy/pywinpty/blob/main/LICENSE.txt) |
| pyte 0.8.2 | Terminal parser, installed by Setup | [LGPL-3.0-or-later](https://github.com/selectel/pyte/blob/master/LICENSE) |
| wcwidth 0.9.1 | Unicode widths, installed by Setup | [MIT](https://github.com/jquast/wcwidth/blob/master/LICENSE) |

Python dependencies are downloaded independently into the user's virtual environment, with their wheel license metadata; they are not bundled in the release ZIP. Included .NET notices come from official runtime/WPF v8.0.28 tags, matching the 1.7.0 release runtime. Check them when changing the runtime selected by the installed SDK; record actual runtime versions for releases.

Segoe UI is supplied by Windows and not redistributed. The generated Prism icon prompt is recorded in `assets/icon-prompt.txt`.
