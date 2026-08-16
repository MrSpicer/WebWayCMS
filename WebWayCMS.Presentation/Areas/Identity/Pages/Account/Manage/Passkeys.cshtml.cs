// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebWayCMS.Presentation.Areas.Identity.Pages.Account.Manage
{
    public class PasskeysModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;

        public PasskeysModel(UserManager<IdentityUser> userManager)
        {
            _userManager = userManager;
        }

        public IList<UserPasskeyInfo> Passkeys { get; set; }

        [TempData]
        public string StatusMessage { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            Passkeys = await _userManager.GetPasskeysAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostRenameAsync(string credentialId, string name)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            if (!TryParseCredentialId(credentialId, out var id))
            {
                ErrorMessage = "The passkey could not be found.";
                return RedirectToPage();
            }

            var passkey = await _userManager.GetPasskeyAsync(user, id);
            if (passkey == null)
            {
                ErrorMessage = "The passkey could not be found.";
                return RedirectToPage();
            }

            passkey.Name = name;
            var result = await _userManager.AddOrUpdatePasskeyAsync(user, passkey);
            StatusMessage = result.Succeeded ? "The passkey was renamed." : "The passkey could not be renamed.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRemoveAsync(string credentialId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            if (!TryParseCredentialId(credentialId, out var id))
            {
                ErrorMessage = "The passkey could not be found.";
                return RedirectToPage();
            }

            var result = await _userManager.RemovePasskeyAsync(user, id);
            StatusMessage = result.Succeeded ? "The passkey was removed." : "The passkey could not be removed.";
            return RedirectToPage();
        }

        private static bool TryParseCredentialId(string credentialId, out byte[] id)
        {
            id = null;
            if (string.IsNullOrEmpty(credentialId))
            {
                return false;
            }

            try
            {
                id = Convert.FromBase64String(credentialId);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
