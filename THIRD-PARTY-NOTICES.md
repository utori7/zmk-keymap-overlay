# Third-party notices

ZMK Keymap Overlay is licensed under the MIT License (see [LICENSE](LICENSE)).
It includes or is built from the following third-party material.

## .NET runtime, WPF and Windows Forms

The released `ZmkOverlay.exe` is a self-contained build and bundles the .NET runtime, WPF and Windows Forms
from Microsoft, which are licensed under the MIT License.

- Source: <https://github.com/dotnet/runtime>, <https://github.com/dotnet/wpf>, <https://github.com/dotnet/winforms>
- The release zip contains their license and third-party notices in the `licenses` folder.

## ZMK Firmware

`data/layouts/corne.json` (the key positions of the sample keyboard) is generated from ZMK's 6-column Corne
layout, and `tests/fixtures/zmk/` contains unmodified files from ZMK. At run time, the app can also download
shield definitions from ZMK when you ask it to.

- Source: <https://github.com/zmkfirmware/zmk>
- License: MIT

```
MIT License

Copyright (c) 2020 The ZMK Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
