﻿// Copyright (c) 2020 Jon P Smith, GitHub: JonPSmith, web: http://www.thereformedprogrammer.net/
// Licensed under MIT license. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BookApp.Domain.Books;
using BookApp.Persistence.CosmosDb.Books;
using BookApp.Persistence.EfCoreSql.Books;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;

namespace BookApp.Infrastructure.Books.CosmosDb.Services
{
    public class BookToCosmosBookService : IBookToCosmosBookService
    {
        private readonly BookDbContext _sqlContext;
        private readonly CosmosDbContext _cosmosContext;

        private bool CosmosNotConfigured => _cosmosContext == null;

        public BookToCosmosBookService(BookDbContext sqlContext, CosmosDbContext cosmosContext)
        {
            _sqlContext = sqlContext ?? throw new ArgumentNullException(nameof(sqlContext));
            _cosmosContext = cosmosContext;
        }

        public async Task AddCosmosBookAsync(int bookId) 
        {
            if (CosmosNotConfigured)  
                return;               

            var cosmosBook = await MapBookToCosmosBookAsync(bookId); 

            if (cosmosBook != null)           
            {
                _cosmosContext.Add(cosmosBook);        
                await CosmosSaveChangesWithChecksAsync 
                    (WhatDoing.Adding, bookId);        
            }
            else
            {
                await DeleteCosmosBookAsync(bookId);  
            }
        }


        public async Task UpdateCosmosBookAsync(int bookId) //#A
        {
            if (CosmosNotConfigured)  //#B
                return;               //#B

            var cosmosBook = await MapBookToCosmosBookAsync(bookId); //#C

            if (cosmosBook != null) //#D
            {
                _cosmosContext.Update(cosmosBook);        //#E
                await CosmosSaveChangesWithChecksAsync(   //#E
                    WhatDoing.Updating, bookId);          //#E
            }
            else
            {
                await DeleteCosmosBookAsync(bookId);
            }
        }
        /***************************************************************
        #A This method is called by the BookUpdated event handler, with the BookId of the SQL book
        #B The Book App can be run without access to Cosmos DB, in which case it exits immediately 
        #C This methods uses a Select method similar to the one used in chapter 2, to a CosmosBook entity class
        #D If the CosmosBook is successfully filled, then it executes the Cosmos update code
        #E This updates the CosmosBook to the cosmosContext and then calls a method to save it to the database
        #F If the SQL book wasn't found we ensure the Cosmos database version was removed
         ***************************************************************/

        public async Task DeleteCosmosBookAsync(int bookId)
        {
            if (CosmosNotConfigured)
                return;

            var cosmosBook = new CosmosBook {BookId = (int)bookId};
            _cosmosContext.Remove(cosmosBook);
            await CosmosSaveChangesWithChecksAsync(WhatDoing.Deleting, bookId);
        }

        public async Task UpdateManyCosmosBookAsync(List<int> bookIds)
        {
            var updatedCosmosBooks = await MapManyBooksToCosmosBookAsync(bookIds);
            foreach (var cosmosBook in updatedCosmosBooks)
            {
                _cosmosContext.Update(cosmosBook);
            }
            await _cosmosContext.SaveChangesAsync();
        }

        private enum WhatDoing {Adding, Updating, Deleting}

        private async Task CosmosSaveChangesWithChecksAsync //#A
            (WhatDoing whatDoing, int bookId)  //#B
        {
            try
            {
                await _cosmosContext.SaveChangesAsync(); 
            }
            catch (CosmosException e) //#C
            {
                if (e.StatusCode == HttpStatusCode.NotFound  //#D
                    && whatDoing == WhatDoing.Updating)      //#D
                {
                    var updateVersion = _cosmosContext   //#E
                        .Find<CosmosBook>(bookId);       //#E
                    _cosmosContext.Entry(updateVersion)  //#E
                        .State = EntityState.Detached;   //#E

                    await AddCosmosBookAsync(bookId);       //#F
                }
                else if (e.StatusCode == HttpStatusCode.NotFound  //#G
                         && whatDoing == WhatDoing.Deleting)      //#G
                {                                                 //#G
                    //Do nothing as already deleted               //#G
                }                                                 //#G
                else        //#H
                {           //#H
                    throw;  //#H
                }           //#H
            }
            catch (DbUpdateException e) //#I
            {
                var cosmosException = e.InnerException as CosmosException;   //#J
                if (cosmosException?.StatusCode == HttpStatusCode.Conflict   //#K
                    && whatDoing == WhatDoing.Adding)                        //#K
                {
                    var updateVersion = _cosmosContext.Find<CosmosBook>(bookId);
                    _cosmosContext.Entry(updateVersion)
                        .State = EntityState.Detached;
                    await UpdateCosmosBookAsync(bookId);
                }
                else
                {
                    throw;
                }
            }
        }
        /**************************************************************
        #A This calls SaveChanges and handles certain states
        #B To do this it needs to know what you are trying to do: Add, Update or Delete
        #C This catches any CosmosExceptions
        #D This catches an attempt to update a CosmosBook and it wasn't there
        #E You need to remove the attempted update otherwise EF Core throws an exception
        #F Then you turn the update into an add
        #G This catches the state where the CosmosBook was already deleted
        #H Otherwise its not an exception state you can handle so your rethrow the exception
        #I If you try to add a new CosmosBook and its already there, then you get an DbUpdateException
        #J The inner exception contains the CosmosException
        #K This catches an Add where there was already and CosmosBook with the same key
         *****************************************************************/

        private async Task<CosmosBook> MapBookToCosmosBookAsync(int? bookId)
        {
            return await MapBookToCosmosBook(_sqlContext.Books
                    .IgnoreQueryFilters()
                    .Where(x => x.BookId == bookId))
                .SingleOrDefaultAsync();
        }

        private async Task<List<CosmosBook>> MapManyBooksToCosmosBookAsync(List<int> bookIds)
        {
            return await MapBookToCosmosBook(_sqlContext.Books
                    .Where(x => bookIds.Contains(x.BookId)))
                .ToListAsync();
        }

        private IQueryable<CosmosBook> MapBookToCosmosBook(IQueryable<Book> books)
        {
            return books
                .Select(p => new CosmosBook
                {
                    BookId = p.BookId,
                    Title = p.Title,
                    PublishedOn = p.PublishedOn,
                    EstimatedDate = p.EstimatedDate,
                    YearPublished = p.PublishedOn.Year,
                    OrgPrice = p.OrgPrice,
                    ActualPrice = p.ActualPrice,
                    PromotionalText = p.PromotionalText,
                    ManningBookUrl = p.ManningBookUrl,

                    AuthorsOrdered = string.Join(", ",
                        p.AuthorsLink
                            .OrderBy(q => q.Order)
                            .Select(q => q.Author.Name)),
                    ReviewsCount = p.Reviews.Count(),
                    ReviewsAverageVotes =
                        p.Reviews.Select(y =>
                            (double?)y.NumStars).Average(),
                    Tags = p.Tags
                        .Select(x => new CosmosTag(x.TagId)).ToList(),
                    TagsString = $"| {string.Join(" | ", p.Tags.Select(x => x.TagId))} |"
                });
        }


    }
}