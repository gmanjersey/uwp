﻿using mega;
using MegaApp.Enums;

namespace MegaApp.MegaApi
{
    class InviteContactRequestListenerAsync : BaseRequestListenerAsync<InviteContactResult>
    {
        #region MRequestListenerInterface

        public override void onRequestFinish(MegaSDK api, MRequest request, MError e)
        {
            base.onRequestFinish(api, request, e);

            if (Tcs.Task.IsFaulted) return;

            if (request.getType() == MRequestType.TYPE_INVITE_CONTACT)
            {
                switch (e.getErrorCode())
                {
                    case MErrorType.API_OK: // Successfull invite contact process
                        Tcs?.TrySetResult(InviteContactResult.Success);
                        break;
                    case MErrorType.API_EEXIST: // Contact already exists
                        Tcs?.TrySetResult(InviteContactResult.AlreadyExists);
                        break;
                    default: // Default error processing
                        Tcs?.TrySetResult(InviteContactResult.Unknown);
                        break;
                }
            }
        }

        #endregion
    }
}
