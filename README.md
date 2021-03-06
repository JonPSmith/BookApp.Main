# BookApp.Main

This is a version of the BookApp used in the [Evolving modular monoliths series](https://www.thereformedprogrammer.net/evolving-modular-monoliths-1-an-architecture-for-net/) of articles. The BookApp is a e-commerce web app taht sells books using ASP.NET Core and EF Core. This application features in my book [Entity Framework Core in Action](https://bit.ly/EfCoreBook2).

This specific version:

- Contains BookApp FrontEnd, the `Orders` bounded context code and
- The [`BookApp.Books`](https://github.com/JonPSmith/BookApp.Books) part of the app is provided via a NuGet package - see the second part of the evolving modular monoliths series for how this was done.  !!! LINK !!! 
- Uses the [modularize bounded context approach](https://www.thereformedprogrammer.net/evolving-modular-monoliths-1-an-architecture-for-net/#3-modularize-inside-a-bounded-context), where each project is focused on one job.