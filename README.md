## ADLX Wrapper for C#

This is a POC of a wrapper for 'AMD Device Library eXtra', without the need of C, C++, the SDK or SWIG. Pure C# in a single source file, see AmdAdlx.cs. Designed for use with 'using' block, or try-finally, for the Dispose to be called and release the pointers in adlx library.
