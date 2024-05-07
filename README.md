## ADLX Wrapper for C#

This is a POC of a wrapper for 'AMD Device Library eXtra', without the need of C, C++, the SDK or SWIG. Pure C# in a single source file, see AmdAdlx.cs. Designed for use with 'using' block, or try-finally, for the Dispose to be called and release the pointers in adlx library.

### Console usage
- Run a infinite loop making gpu readings, no interval. Without fps counter or adl mapping. Useful to find memory leaks. **ONLY reads, nothing is written to the driver**.
```
adlxwrapper-console.exe loop nofps noadl
```

- Normal execution with debug messages (mostly pointer release messages), shows metrics, gpu info, features support, etc. Without fps counter or adl mapping.
```
adlxwrapper-console.exe debug nofps noadl
```

- Normal execution with debug messages (mostly pointer release messages), shows metrics, gpu info, features support, etc.
```
adlxwrapper-console.exe debug
```

- Normal execution with debug messages (mostly pointer release messages), set fans rpm.
```
adlxwrapper-console.exe debug setrpm
```
