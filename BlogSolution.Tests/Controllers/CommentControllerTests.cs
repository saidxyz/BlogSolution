﻿using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Identity;
using BlogSolution.Controllers;
using BlogSolution.Data;
using BlogSolution.Models;
using BlogSolution.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System;

namespace BlogSolution.Tests.Controllers
{
    public class CommentControllerTests : IDisposable
    {
        private readonly Mock<UserManager<IdentityUser>> _userManagerMock;
        private readonly Mock<ILogger<CommentController>> _loggerMock;
        private readonly Mock<IAuthorizationService> _authorizationServiceMock;
        private readonly ApplicationDbContext _context;
        private readonly string _databaseName;

        public CommentControllerTests()
        {
            // Generer et unikt databasenavn for hver test
            _databaseName = Guid.NewGuid().ToString();

            var store = new Mock<IUserStore<IdentityUser>>();
            _userManagerMock = new Mock<UserManager<IdentityUser>>(
                store.Object, null, null, null, null, null, null, null, null);

            _loggerMock = new Mock<ILogger<CommentController>>();
            _authorizationServiceMock = new Mock<IAuthorizationService>();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: _databaseName)
                .Options;
            _context = new ApplicationDbContext(options);
        }

       
        /// Hjelpemetode for å simulere en autentisert bruker.
        
        private ClaimsPrincipal GetUserPrincipal(string userId, string userName)
        {
            var userClaims = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, userName),
            }, "mock"));

            return userClaims;
        }

       
        /// Tester at Create (GET) returnerer riktig View med korrekt modell.
        
        [Fact]
        public void Create_Get_ReturnsViewWithCorrectModel()
        {
            // Arrange
            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);
            int postId = 1;

            // Act
            var result = controller.Create(postId);

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<CommentCreateViewModel>(viewResult.Model);
            Assert.Equal(postId, model.PostId);
        }

        
        /// Tester at Create (POST) med gyldig modell oppretter en kommentar og omdirigerer til Post Details.
        
        [Fact]
        public async Task Create_Post_ValidModel_RedirectsToPostDetails()
        {
            // Arrange
            var user = new IdentityUser 
            { 
                Id = "test-user-id", 
                UserName = "test@example.com", 
                Email = "test@example.com" 
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Opprett en Post som kommentaren refererer til med riktig UserId
            var post = new Post 
            { 
                Id = 1, 
                Title = "Test Post", 
                Content = "Content of the test post.", 
                UserId = user.Id // Sett UserId til eieren
            };
            _context.Posts.Add(post);
            await _context.SaveChangesAsync();

            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);

            var model = new CommentCreateViewModel
            {
                Content = "This is a test comment.",
                PostId = 1
            };

            // Mock UserManager til å returnere user når GetUserAsync kalles
            _userManagerMock.Setup(um => um.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(user);

            // Simuler en autentisert bruker
            controller.ControllerContext = new ControllerContext()
            {
                HttpContext = new DefaultHttpContext() { User = GetUserPrincipal(user.Id, user.UserName) }
            };

            // Act
            var result = await controller.Create(model);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Details", redirectResult.ActionName);
            Assert.Equal("Post", redirectResult.ControllerName);
            Assert.Equal(1, redirectResult.RouteValues["id"]);

            // Sjekk at kommentaren ble lagt til i databasen
            var comment = await _context.Comments.FirstOrDefaultAsync(c => c.Content == "This is a test comment.");
            Assert.NotNull(comment);
            Assert.Equal(user.Id, comment.UserId);
            Assert.Equal(1, comment.PostId);
            Assert.NotEqual(0, comment.Id); // Sikre at Id er tildelt
        }

       
        /// Tester at Create (POST) med ugyldig modell returnerer View med samme modell.
        
        [Fact]
        public async Task Create_Post_InvalidModel_ReturnsView()
        {
            // Arrange
            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);
            controller.ModelState.AddModelError("Content", "Required");

            var model = new CommentCreateViewModel
            {
                Content = "", // Ugyldig fordi det er påkrevd
                PostId = 1
            };

            // Act
            var result = await controller.Create(model);

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal(model, viewResult.Model);
        }

       
        /// Tester at Edit (GET) med gyldig id og autorisert bruker returnerer View med korrekt modell.
        
        [Fact]
        public async Task Edit_Get_ValidId_Authorized_ReturnsViewWithCorrectModel()
        {
            // Arrange
            var user = new IdentityUser 
            { 
                Id = "user-id", 
                UserName = "user@example.com", 
                Email = "user@example.com" 
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Legg til en Post som kommentaren refererer til med riktig UserId
            var post = new Post 
            { 
                Id = 1, 
                Title = "Test Post", 
                Content = "Content of the test post.", 
                UserId = user.Id // Sett UserId
            };
            _context.Posts.Add(post);
            await _context.SaveChangesAsync();

            var comment = new Comment 
            { 
                Id = 1, 
                Content = "Original Comment", 
                PostId = 1, 
                UserId = user.Id 
            };
            _context.Comments.Add(comment);
            await _context.SaveChangesAsync();

            _authorizationServiceMock.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), comment, "IsCommentOwner"))
                .ReturnsAsync(AuthorizationResult.Success());

            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);

            // Simuler en autentisert bruker
            controller.ControllerContext = new ControllerContext()
            {
                HttpContext = new DefaultHttpContext() { User = GetUserPrincipal(user.Id, user.UserName) }
            };

            // Act
            var result = await controller.Edit(comment.Id);

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<CommentEditViewModel>(viewResult.Model);
            Assert.Equal(comment.Content, model.Content);
            Assert.Equal(comment.PostId, model.PostId);
        }

       
        /// Test that Edit action with null id returns NotFound.
        
        [Fact]
        public async Task Edit_Get_NullId_ReturnsNotFound()
        {
            // Arrange
            var controller = new CommentController(
                _context,
                _userManagerMock.Object,
                _loggerMock.Object,
                _authorizationServiceMock.Object);

            // Act
            var result = await controller.Edit(null);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }
       
        /// Test that Edit action with non-existent comment id returns NotFound.
        
        [Fact]
        public async Task Edit_Get_CommentNotFound_ReturnsNotFound()
        {
            // Arrange
            int nonExistentCommentId = 999;
            var controller = new CommentController(
                _context,
                _userManagerMock.Object,
                _loggerMock.Object,
                _authorizationServiceMock.Object);

            // Act
            var result = await controller.Edit(nonExistentCommentId);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }
       
        /// Test that Edit action with unauthorized user returns Forbid.
        
        [Fact]
        public async Task Edit_Get_UnauthorizedUser_ReturnsForbid()
        {
            // Arrange
            var ownerUser = new IdentityUser
            {
                Id = "owner-user-id",
                UserName = "owneruser",
                Email = "owner@example.com"
            };
            var otherUser = new IdentityUser
            {
                Id = "other-user-id",
                UserName = "otheruser",
                Email = "other@example.com"
            };
            _context.Users.Add(ownerUser);
            _context.Users.Add(otherUser);
            await _context.SaveChangesAsync();

            var comment = new Comment
            {
                Id = 1,
                Content = "Original Comment",
                PostId = 1,
                UserId = ownerUser.Id,
                CreatedAt = DateTime.UtcNow
            };
            _context.Comments.Add(comment);
            await _context.SaveChangesAsync();

            // Mock authorization to fail
            _authorizationServiceMock.Setup(a =>
                    a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), comment, "IsCommentOwner"))
                .ReturnsAsync(AuthorizationResult.Failed());

            var controller = new CommentController(
                _context,
                _userManagerMock.Object,
                _loggerMock.Object,
                _authorizationServiceMock.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = GetUserPrincipal(otherUser.Id, otherUser.UserName) }
                }
            };

            // Act
            var result = await controller.Edit(comment.Id);

            // Assert
            Assert.IsType<ForbidResult>(result);
        }
       
        /// Test that Edit action with authorized user returns View with model.
        
        [Fact]
        public async Task Edit_Get_AuthorizedUser_ReturnsViewWithModel()
        {
            // Arrange
            var user = new IdentityUser
            {
                Id = "user-id",
                UserName = "testuser",
                Email = "testuser@example.com"
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var comment = new Comment
            {
                Id = 1,
                Content = "Original Comment",
                PostId = 1,
                UserId = user.Id,
                CreatedAt = DateTime.UtcNow
            };
            _context.Comments.Add(comment);
            await _context.SaveChangesAsync();

            // Mock authorization to succeed
            _authorizationServiceMock.Setup(a =>
                    a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), comment, "IsCommentOwner"))
                .ReturnsAsync(AuthorizationResult.Success());

            var controller = new CommentController(
                _context,
                _userManagerMock.Object,
                _loggerMock.Object,
                _authorizationServiceMock.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = GetUserPrincipal(user.Id, user.UserName) }
                }
            };

            // Act
            var result = await controller.Edit(comment.Id);

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<CommentEditViewModel>(viewResult.Model);
            Assert.Equal(comment.Content, model.Content);
            Assert.Equal(comment.PostId, model.PostId);
        }
       
        /// Tester at Edit (GET) med ugyldig id returnerer NotFound.
        
        [Fact]
        public async Task Edit_Get_InvalidId_ReturnsNotFound()
        {
            // Arrange
            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);
            int invalidId = 999;

            // Act
            var result = await controller.Edit(invalidId);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

       
        /// Tester at Edit (GET) med gyldig id, men uautorisert bruker returnerer Forbid.
        
        [Fact]
        public async Task Edit_Get_ValidId_Unauthorized_ReturnsForbid()
        {
            // Arrange
            var user = new IdentityUser 
            { 
                Id = "user-id", 
                UserName = "user@example.com", 
                Email = "user@example.com" 
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Legg til en Post som kommentaren refererer til med riktig UserId
            var post = new Post 
            { 
                Id = 1, 
                Title = "Test Post", 
                Content = "Content of the test post.", 
                UserId = user.Id // Sett UserId
            };
            _context.Posts.Add(post);
            await _context.SaveChangesAsync();

            var comment = new Comment 
            { 
                Id = 1, 
                Content = "Original Comment", 
                PostId = 1, 
                UserId = "other-user-id" // Setter til en annen bruker for å simulere uautorisert tilgang
            };
            _context.Comments.Add(comment);
            await _context.SaveChangesAsync();

            _authorizationServiceMock.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), comment, "IsCommentOwner"))
                .ReturnsAsync(AuthorizationResult.Failed());

            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);

            // Simuler en autentisert bruker
            controller.ControllerContext = new ControllerContext()
            {
                HttpContext = new DefaultHttpContext() { User = GetUserPrincipal(user.Id, user.UserName) }
            };

            // Act
            var result = await controller.Edit(comment.Id);

            // Assert
            Assert.IsType<ForbidResult>(result);
        }

       
        /// Tester at Edit (POST) med gyldig modell oppdaterer kommentaren og omdirigerer til Post Details.
        
        [Fact]
        public async Task Edit_Post_ValidModel_Authorized_RedirectsToPostDetails()
        {
            // Arrange
            var user = new IdentityUser 
            { 
                Id = "user-id", 
                UserName = "user@example.com", 
                Email = "user@example.com" 
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Legg til en Post som kommentaren refererer til med riktig UserId
            var post = new Post 
            { 
                Id = 1, 
                Title = "Test Post", 
                Content = "Content of the test post.", 
                UserId = user.Id // Sett UserId
            };
            _context.Posts.Add(post);
            await _context.SaveChangesAsync();

            var comment = new Comment 
            { 
                Id = 1, 
                Content = "Original Comment", 
                PostId = 1, 
                UserId = user.Id 
            };
            _context.Comments.Add(comment);
            await _context.SaveChangesAsync();

            _authorizationServiceMock.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), comment, "IsCommentOwner"))
                .ReturnsAsync(AuthorizationResult.Success());

            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);

            var model = new CommentEditViewModel
            {
                Content = "Updated Comment",
                PostId = 1
            };

            // Simuler en autentisert bruker
            controller.ControllerContext = new ControllerContext()
            {
                HttpContext = new DefaultHttpContext() { User = GetUserPrincipal(user.Id, user.UserName) }
            };

            // Act
            var result = await controller.Edit(comment.Id, model);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Details", redirectResult.ActionName);
            Assert.Equal("Post", redirectResult.ControllerName);
            Assert.Equal(1, redirectResult.RouteValues["id"]);

            // Sjekk at kommentaren ble oppdatert i databasen
            var updatedComment = await _context.Comments.FindAsync(comment.Id);
            Assert.Equal("Updated Comment", updatedComment.Content);
        }

       
        /// Tester at Edit (POST) med ugyldig modell returnerer View med samme modell.
        
        [Fact]
        public async Task Edit_Post_InvalidModel_ReturnsView()
        {
            // Arrange
            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);
            controller.ModelState.AddModelError("Content", "Required");

            var model = new CommentEditViewModel
            {
                Content = "", // Ugyldig fordi det er påkrevd
                PostId = 1
            };

            // Act
            var result = await controller.Edit(1, model);

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal(model, viewResult.Model);
        }

       
        /// Tester at Edit (POST) med gyldig modell, men uautorisert bruker returnerer Forbid.
        
        [Fact]
        public async Task Edit_Post_ValidModel_Unauthorized_ReturnsForbid()
        {
            // Arrange
            var user = new IdentityUser 
            { 
                Id = "user-id", 
                UserName = "user@example.com", 
                Email = "user@example.com" 
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Legg til en Post som kommentaren refererer til med riktig UserId
            var post = new Post 
            { 
                Id = 1, 
                Title = "Test Post", 
                Content = "Content of the test post.", 
                UserId = user.Id // Sett UserId
            };
            _context.Posts.Add(post);
            await _context.SaveChangesAsync();

            var comment = new Comment 
            { 
                Id = 1, 
                Content = "Original Comment", 
                PostId = 1, 
                UserId = "other-user-id" // Setter til en annen bruker for å simulere uautorisert tilgang
            };
            _context.Comments.Add(comment);
            await _context.SaveChangesAsync();

            _authorizationServiceMock.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), comment, "IsCommentOwner"))
                .ReturnsAsync(AuthorizationResult.Failed());

            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);

            var model = new CommentEditViewModel
            {
                Content = "Updated Comment",
                PostId = 1
            };

            // Simuler en autentisert bruker
            controller.ControllerContext = new ControllerContext()
            {
                HttpContext = new DefaultHttpContext() { User = GetUserPrincipal(user.Id, user.UserName) }
            };

            // Act
            var result = await controller.Edit(comment.Id, model);

            // Assert
            Assert.IsType<ForbidResult>(result);

            // Sjekk at kommentaren ikke ble oppdatert
            var existingComment = await _context.Comments.FindAsync(comment.Id);
            Assert.Equal("Original Comment", existingComment.Content);
        }

       
        /// Tester at Edit (POST) med gyldig modell, men kommentar ikke funnet returnerer NotFound.
        
        [Fact]
        public async Task Edit_Post_ValidModel_CommentNotFound_ReturnsNotFound()
        {
            // Arrange
            var user = new IdentityUser 
            { 
                Id = "user-id", 
                UserName = "user@example.com", 
                Email = "user@example.com" 
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Legg til en Post som kommentaren refererer til med riktig UserId
            var post = new Post 
            { 
                Id = 1, 
                Title = "Test Post", 
                Content = "Content of the test post.", 
                UserId = user.Id // Sett UserId
            };
            _context.Posts.Add(post);
            await _context.SaveChangesAsync();

            var model = new CommentEditViewModel
            {
                Content = "Updated Comment",
                PostId = 1
            };

            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);

            // Simuler en autentisert bruker
            controller.ControllerContext = new ControllerContext()
            {
                HttpContext = new DefaultHttpContext() { User = GetUserPrincipal(user.Id, user.UserName) }
            };

            // Act
            var result = await controller.Edit(999, model); // Kommentar med id 999 finnes ikke

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

       
/// Tester at Edit (POST) med gyldig modell, men en koncurrensfeil oppstår, returnerer NotFound.

[Fact]
public async Task Edit_Post_ConcurrencyException_ReturnsNotFound()
{
    // Arrange
    var user = new IdentityUser 
    { 
        Id = "user-id", 
        UserName = "user@example.com", 
        Email = "user@example.com" 
    };
    _context.Users.Add(user);
    await _context.SaveChangesAsync();

    // Legg til en Post som kommentaren refererer til med riktig UserId
    var post = new Post 
    { 
        Id = 1, 
        Title = "Test Post", 
        Content = "Content of the test post.", 
        UserId = user.Id // Sett UserId
    };
    _context.Posts.Add(post);
    await _context.SaveChangesAsync();

    var comment = new Comment 
    { 
        Id = 1, 
        Content = "Original Comment", 
        PostId = 1, 
        UserId = user.Id 
    };
    _context.Comments.Add(comment);
    await _context.SaveChangesAsync();

    _authorizationServiceMock.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), comment, "IsCommentOwner"))
        .ReturnsAsync(AuthorizationResult.Success());

    var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);

    var model = new CommentEditViewModel
    {
        Content = "Updated Comment",
        PostId = 1
    };

    // Simuler en autentisert bruker
    controller.ControllerContext = new ControllerContext()
    {
        HttpContext = new DefaultHttpContext() { User = GetUserPrincipal(user.Id, user.UserName) }
    };

    // Simulere koncurrensfeil ved å fjerne kommentaren før oppdatering
    _context.Comments.Remove(comment);
    await _context.SaveChangesAsync(); // Kommentaren er allerede fjernet

    // Act
    var result = await controller.Edit(comment.Id, model);

    // Assert
    Assert.IsType<NotFoundResult>(result);

    // Valgfritt: Sjekk at kommentaren fortsatt ikke finnes i databasen
    var existingComment = await _context.Comments.FindAsync(comment.Id);
    Assert.Null(existingComment);
}


       
        /// Tester at Delete (GET) med gyldig id og autorisert bruker returnerer View med korrekt modell.
        
        [Fact]
        public async Task Delete_Get_ValidId_Authorized_ReturnsViewWithCorrectModel()
        {
            // Arrange
            var user = new IdentityUser 
            { 
                Id = "user-id", 
                UserName = "user@example.com", 
                Email = "user@example.com" 
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Legg til en Post som kommentaren refererer til med riktig UserId
            var post = new Post 
            { 
                Id = 1, 
                Title = "Test Post", 
                Content = "Content of the test post.", 
                UserId = user.Id // Sett UserId
            };
            _context.Posts.Add(post);
            await _context.SaveChangesAsync();

            var comment = new Comment 
            { 
                Id = 1, 
                Content = "Comment to delete", 
                PostId = 1, 
                UserId = user.Id 
            };
            _context.Comments.Add(comment);
            await _context.SaveChangesAsync();

            _authorizationServiceMock.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), comment, "IsCommentOwner"))
                .ReturnsAsync(AuthorizationResult.Success());

            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);

            // Simuler en autentisert bruker
            controller.ControllerContext = new ControllerContext()
            {
                HttpContext = new DefaultHttpContext() { User = GetUserPrincipal(user.Id, user.UserName) }
            };

            // Act
            var result = await controller.Delete(comment.Id);

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<Comment>(viewResult.Model);
            Assert.Equal(comment.Id, model.Id);
            Assert.Equal(comment.Content, model.Content);
            Assert.Equal(comment.PostId, model.PostId);
        }

       
        /// Tester at Delete (GET) med null id returnerer NotFound.
        
        [Fact]
        public async Task Delete_Get_NullId_ReturnsNotFound()
        {
            // Arrange
            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);

            // Act
            var result = await controller.Delete(null);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

       
        /// Tester at Delete (GET) med ugyldig id returnerer NotFound.
        
        [Fact]
        public async Task Delete_Get_InvalidId_ReturnsNotFound()
        {
            // Arrange
            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);
            int invalidId = 999;

            // Act
            var result = await controller.Delete(invalidId);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

/// Tester at Delete (GET) med gyldig id, men uautorisert bruker returnerer Forbid.

[Fact]
public async Task Delete_Get_ValidId_Unauthorized_ReturnsForbid()
{
    // Arrange
    var ownerUser = new IdentityUser 
    { 
        Id = "owner-user-id", 
        UserName = "owner@example.com", 
        Email = "owner@example.com" 
    };
    var otherUser = new IdentityUser 
    { 
        Id = "other-user-id", 
        UserName = "other@example.com", 
        Email = "other@example.com" 
    };
    _context.Users.Add(ownerUser);
    _context.Users.Add(otherUser);
    await _context.SaveChangesAsync();

    // Opprett en Post som kommentaren refererer til med riktig UserId
    var post = new Post 
    { 
        Id = 1, 
        Title = "Test Post", 
        Content = "Content of the test post.", 
        UserId = ownerUser.Id // Sett UserId til eieren
    };
    _context.Posts.Add(post);
    await _context.SaveChangesAsync();

    // Opprett en kommentar knyttet til posten og eier-brukeren
    var comment = new Comment 
    { 
        Id = 1, 
        Content = "Comment to delete", 
        PostId = 1, 
        UserId = ownerUser.Id // Kommentar eies av ownerUser
    };
    _context.Comments.Add(comment);
    await _context.SaveChangesAsync();

    // Mock autorisasjon for å returnere feilet resultat når andreUser prøver å slette
    _authorizationServiceMock.Setup(a => a.AuthorizeAsync(
        It.IsAny<ClaimsPrincipal>(), 
        It.Is<Comment>(c => c.Id == comment.Id), 
        "IsCommentOwner"))
        .ReturnsAsync(AuthorizationResult.Failed());

    var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);

    // Simuler en autentisert annen bruker (otherUser)
    controller.ControllerContext = new ControllerContext()
    {
        HttpContext = new DefaultHttpContext() { User = GetUserPrincipal(otherUser.Id, otherUser.UserName) }
    };

    // Act
    var result = await controller.Delete(comment.Id);

    // Assert
    Assert.IsType<ForbidResult>(result);
}


       
        /// Tester at DeleteConfirmed (POST) med gyldig id og autorisert bruker sletter kommentaren og omdirigerer til Post Details.
        
        [Fact]
        public async Task DeleteConfirmed_Post_ValidId_Authorized_RedirectsToPostDetails()
        {
            // Arrange
            var user = new IdentityUser 
            { 
                Id = "user-id", 
                UserName = "user@example.com", 
                Email = "user@example.com" 
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Legg til en Post som kommentaren refererer til med riktig UserId
            var post = new Post 
            { 
                Id = 1, 
                Title = "Test Post", 
                Content = "Content of the test post.", 
                UserId = user.Id // Sett UserId
            };
            _context.Posts.Add(post);
            await _context.SaveChangesAsync();

            var comment = new Comment 
            { 
                Id = 1, 
                Content = "Comment to delete", 
                PostId = 1, 
                UserId = user.Id 
            };
            _context.Comments.Add(comment);
            await _context.SaveChangesAsync();

            _authorizationServiceMock.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), comment, "IsCommentOwner"))
                .ReturnsAsync(AuthorizationResult.Success());

            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);

            // Simuler en autentisert bruker
            controller.ControllerContext = new ControllerContext()
            {
                HttpContext = new DefaultHttpContext() { User = GetUserPrincipal(user.Id, user.UserName) }
            };

            // Act
            var result = await controller.DeleteConfirmed(comment.Id);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Details", redirectResult.ActionName);
            Assert.Equal("Post", redirectResult.ControllerName);
            Assert.Equal(1, redirectResult.RouteValues["id"]);

            // Sjekk at kommentaren ble fjernet fra databasen
            var deletedComment = await _context.Comments.FindAsync(comment.Id);
            Assert.Null(deletedComment);
        }

       
        /// Tester at DeleteConfirmed (POST) med ugyldig id returnerer NotFound.
        
        [Fact]
        public async Task DeleteConfirmed_Post_InvalidId_ReturnsNotFound()
        {
            // Arrange
            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);
            int invalidId = 999;

            // Act
            var result = await controller.DeleteConfirmed(invalidId);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

       
        /// Tester at DeleteConfirmed (POST) med gyldig id, men uautorisert bruker returnerer Forbid.
        
        [Fact]
        public async Task DeleteConfirmed_Post_ValidId_Unauthorized_ReturnsForbid()
        {
            // Arrange
            var user = new IdentityUser 
            { 
                Id = "user-id", 
                UserName = "user@example.com", 
                Email = "user@example.com" 
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Legg til en Post som kommentaren refererer til med riktig UserId
            var post = new Post 
            { 
                Id = 1, 
                Title = "Test Post", 
                Content = "Content of the test post.", 
                UserId = user.Id // Sett UserId
            };
            _context.Posts.Add(post);
            await _context.SaveChangesAsync();

            var comment = new Comment 
            { 
                Id = 1, 
                Content = "Comment to delete", 
                PostId = 1, 
                UserId = "other-user-id" // Setter til en annen bruker for å simulere uautorisert tilgang
            };
            _context.Comments.Add(comment);
            await _context.SaveChangesAsync();

            _authorizationServiceMock.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), comment, "IsCommentOwner"))
                .ReturnsAsync(AuthorizationResult.Failed());

            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);

            // Simuler en autentisert bruker
            controller.ControllerContext = new ControllerContext()
            {
                HttpContext = new DefaultHttpContext() { User = GetUserPrincipal(user.Id, user.UserName) }
            };

            // Act
            var result = await controller.DeleteConfirmed(comment.Id);

            // Assert
            Assert.IsType<ForbidResult>(result);

            // Sjekk at kommentaren ikke ble fjernet fra databasen
            var existingComment = await _context.Comments.FindAsync(comment.Id);
            Assert.NotNull(existingComment);
        }

       
        /// Tester at DeleteConfirmed (POST) med gyldig id, men en koncurrensfeil oppstår, returnerer feil.
        
       
        /// Tester at DeleteConfirmed (POST) med gyldig id, men en koncurrensfeil oppstår, returnerer NotFound.
        
        [Fact]
        public async Task DeleteConfirmed_Post_ConcurrencyException_ReturnsNotFound()
        {
            // Arrange
            var user = new IdentityUser 
            { 
                Id = "user-id", 
                UserName = "user@example.com", 
                Email = "user@example.com" 
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Legg til en Post som kommentaren refererer til med riktig UserId
            var post = new Post 
            { 
                Id = 1, 
                Title = "Test Post", 
                Content = "Content of the test post.", 
                UserId = user.Id // Sett UserId
            };
            _context.Posts.Add(post);
            await _context.SaveChangesAsync();

            var comment = new Comment 
            { 
                Id = 1, 
                Content = "Comment to delete", 
                PostId = 1, 
                UserId = user.Id 
            };
            _context.Comments.Add(comment);
            await _context.SaveChangesAsync();

            _authorizationServiceMock.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), comment, "IsCommentOwner"))
                .ReturnsAsync(AuthorizationResult.Success());

            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);

            // Simuler en autentisert bruker
            controller.ControllerContext = new ControllerContext()
            {
                HttpContext = new DefaultHttpContext() { User = GetUserPrincipal(user.Id, user.UserName) }
            };

            // Simulere koncurrensfeil ved å fjerne kommentaren før sletting
            _context.Comments.Remove(comment);
            await _context.SaveChangesAsync(); // Kommentaren er allerede fjernet

            // Act
            var result = await controller.DeleteConfirmed(comment.Id);

            // Assert
            Assert.IsType<NotFoundResult>(result);

            // Sjekk at kommentaren fortsatt ikke finnes i databasen
            var existingComment = await _context.Comments.FindAsync(comment.Id);
            Assert.Null(existingComment);
        }
        
        
        [Fact]
        public async Task Create_Post_ValidModel_UserFound_CreatesCommentAndRedirects()
        {
            // Arrange
            var user = new IdentityUser
            {
                Id = "user-id",
                UserName = "testuser",
                Email = "testuser@example.com"
            };

            // Mock UserManager to return the user
            _userManagerMock.Setup(um => um.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(user);

            // Add necessary data to the context
            var post = new Post
            {
                Id = 1,
                Title = "Test Post",
                Content = "Test Content",
                UserId = user.Id,
                CreatedAt = DateTime.UtcNow
            };
            _context.Posts.Add(post);
            await _context.SaveChangesAsync();

            var model = new CommentCreateViewModel
            {
                Content = "Test Comment",
                PostId = post.Id
            };

            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = GetUserPrincipal(user.Id, user.UserName) }
                }
            };

            // Act
            var result = await controller.Create(model);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Details", redirectResult.ActionName);
            Assert.Equal("Post", redirectResult.ControllerName);
            Assert.Equal(post.Id, redirectResult.RouteValues["id"]);

            // Verify that the comment was added to the database
            var comment = await _context.Comments.FirstOrDefaultAsync(c => c.Content == "Test Comment");
            Assert.NotNull(comment);
            Assert.Equal(user.Id, comment.UserId);
            Assert.Equal(post.Id, comment.PostId);
        }

        [Fact]
        public async Task Create_Post_ValidModel_UserNotFound_RedirectsToLogin()
        {
            // Arrange
            // Mock UserManager to return null
            _userManagerMock.Setup(um => um.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync((IdentityUser)null);

            var model = new CommentCreateViewModel
            {
                Content = "Test Comment",
                PostId = 1
            };

            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
                }
            };

            // Act
            var result = await controller.Create(model);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirectResult.ActionName);
            Assert.Equal("Account", redirectResult.ControllerName);
        }

        [Fact]
        public async Task Create_Post_InvalidModel_ReturnsViewWithModel()
        {
            // Arrange
            var controller = new CommentController(_context, _userManagerMock.Object, _loggerMock.Object, _authorizationServiceMock.Object);

            // Simulate invalid ModelState
            controller.ModelState.AddModelError("Content", "Content is required");

            var model = new CommentCreateViewModel
            {
                Content = "", // Invalid because content is required
                PostId = 1
            };

            // Act
            var result = await controller.Create(model);

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            var returnedModel = Assert.IsType<CommentCreateViewModel>(viewResult.Model);
            Assert.Equal(model.PostId, returnedModel.PostId);
            Assert.Equal(model.Content, returnedModel.Content);
        }

       
        /// Hjelpe-metode for å sjekke om en kommentar eksisterer.
        
        private bool CommentExists(int id)
        {
            return _context.Comments.Any(e => e.Id == id);
        }

       
        /// Rydd opp etter tester ved å slette databasen.
        
        public void Dispose()
        {
            _context.Dispose();
        }
    }
}
