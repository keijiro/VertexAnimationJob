**VertexAnimationJob** is a Unity project that contains examples of the use of
the new Mesh API added in Unity 2019.3/2020.1.

See the following documents for the details of the new APIs.

- [2019.3 Mesh API Improvements](https://drive.google.com/open?id=1I225X6jAxWN0cheDz_3gnhje3hWNMxTZq3FZQs5KqPc)
- [2020.1 Mesh API improvements](https://drive.google.com/open?id=1QC7NV7JQcvibeelORJvsaTReTyszllOlxdfEsaVL2oA)

This project aims to show how to implement vertex animation using the new APIs
along with the C# Job System. It distributes vertex update jobs to multiple
worker threads via the job system. With the help of the new API, it can be
implemented with a minimum amount of memory copies.
