# Third-party notices

The C# application uses these direct NuGet dependencies:

| Package | Version | License | Source |
| --- | --- | --- | --- |
| `RustPlusApi` | `2.0.0-beta.10` | MIT | [HandyS11/RustPlusApi](https://github.com/HandyS11/RustPlusApi) |
| `RustPlusApi.Fcm` | `2.0.0-beta.10` | MIT | [HandyS11/RustPlusApi](https://github.com/HandyS11/RustPlusApi) |
| `RustPlusApi.Fcm.Registration` | `2.0.0-beta.10` | MIT | [HandyS11/RustPlusApi](https://github.com/HandyS11/RustPlusApi) |

The RustPlusApi packages include the following MIT license notice:

```text
MIT License

Copyright (c) 2024 Clergue Valentin

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

Current transitive dependencies include `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Logging.Abstractions` (MIT, Microsoft), plus `protobuf-net` and `protobuf-net.Core` (Apache-2.0, Marc Gravell). Their package metadata and license texts are available from [NuGet](https://www.nuget.org/) and the respective [Microsoft](https://github.com/dotnet/dotnet) and [protobuf-net](https://github.com/protobuf-net/protobuf-net) repositories. Include their license texts with any binary release that redistributes these dependencies.
