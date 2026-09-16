// Media Trip Helper · iOS Files picker plugin.
// Compiled by Xcode at build time on the Mac. Three entry points, called from C#
// (Assets/UI/Scripts/Transfer/FilePickers.cs, guarded by UNITY_IOS && !UNITY_EDITOR):
//
//   _MediaTrip_ImportFile(extensionsCsv)  UIDocumentPickerViewController in open mode, filtered
//                                          to the given extensions. The chosen file is read under
//                                          security-scoped access and copied into the app's own
//                                          tmp/MediaTripImport folder; that local path is sent
//                                          back to Unity.
//   _MediaTrip_ExportFile(path)            UIDocumentPickerViewController in export mode (Files,
//                                          iCloud Drive, other providers).
//   _MediaTrip_ShareFile(path)             UIActivityViewController (AirDrop, Mail, ...).
//
// Results arrive on the GameObject "MediaTripNative":
//   OnFileImported(path) · OnFileImportCancelled(reason) · OnExportDone(result) ·
//   OnExportCancelled(reason) · OnNativeError(message)
// Every failure path sends a message; nothing fails silently.

#import <UIKit/UIKit.h>
#import <Foundation/Foundation.h>
#if __has_include(<UniformTypeIdentifiers/UniformTypeIdentifiers.h>)
#import <UniformTypeIdentifiers/UniformTypeIdentifiers.h>
#endif
#import "UnityAppController.h"

static const char* kReceiver = "MediaTripNative";

static void MTSend(const char* method, NSString* payload)
{
    UnitySendMessage(kReceiver, method, payload ? payload.UTF8String : "");
}

@interface MediaTripPickerDelegate : NSObject <UIDocumentPickerDelegate>
@property (nonatomic, assign) BOOL exporting;
@end

static MediaTripPickerDelegate* gPickerDelegate = nil;

@implementation MediaTripPickerDelegate

- (void)documentPicker:(UIDocumentPickerViewController*)controller didPickDocumentsAtURLs:(NSArray<NSURL*>*)urls
{
    if (self.exporting) { MTSend("OnExportDone", @"ok"); return; }
    NSURL* url = urls.firstObject;
    if (url == nil) { MTSend("OnFileImportCancelled", @"no file"); return; }

    // The URL may point outside the sandbox (iCloud Drive, another provider). Access it under
    // its security scope, copy it into our own storage, and release the scope.
    BOOL secured = [url startAccessingSecurityScopedResource];
    NSFileManager* fm = [NSFileManager defaultManager];
    NSString* dir = [NSTemporaryDirectory() stringByAppendingPathComponent:@"MediaTripImport"];
    [fm createDirectoryAtPath:dir withIntermediateDirectories:YES attributes:nil error:nil];
    NSString* dest = [dir stringByAppendingPathComponent:url.lastPathComponent];
    [fm removeItemAtPath:dest error:nil];

    __block BOOL ok = NO;
    __block NSError* copyError = nil;
    NSError* coordError = nil;
    NSFileCoordinator* coordinator = [[NSFileCoordinator alloc] initWithFilePresenter:nil];
    [coordinator coordinateReadingItemAtURL:url
                                    options:NSFileCoordinatorReadingWithoutChanges
                                      error:&coordError
                                 byAccessor:^(NSURL* readURL) {
        ok = [fm copyItemAtURL:readURL toURL:[NSURL fileURLWithPath:dest] error:&copyError];
    }];
    if (secured) [url stopAccessingSecurityScopedResource];

    if (ok) {
        MTSend("OnFileImported", dest);
    } else {
        NSError* err = copyError ?: coordError;
        MTSend("OnNativeError", [NSString stringWithFormat:@"Could not copy %@: %@", url.lastPathComponent, err.localizedDescription ?: @"unknown error"]);
    }
}

- (void)documentPickerWasCancelled:(UIDocumentPickerViewController*)controller
{
    MTSend(self.exporting ? "OnExportCancelled" : "OnFileImportCancelled", @"cancel");
}

@end

static UIViewController* MTRootController(void)
{
    UIViewController* vc = UnityGetGLViewController();
    if (vc == nil) vc = [UIApplication sharedApplication].keyWindow.rootViewController;
    return vc;
}

static void MTPresent(UIViewController* picker)
{
    UIViewController* root = MTRootController();
    if (root == nil) { MTSend("OnNativeError", @"No view controller to present the picker from."); return; }
    if (picker.popoverPresentationController != nil) {
        picker.popoverPresentationController.sourceView = root.view;
        picker.popoverPresentationController.sourceRect = CGRectMake(CGRectGetMidX(root.view.bounds), CGRectGetMidY(root.view.bounds), 1, 1);
        picker.popoverPresentationController.permittedArrowDirections = 0;
    }
    [root presentViewController:picker animated:YES completion:nil];
}

extern "C" {

void _MediaTrip_ImportFile(const char* extensionsCsv)
{
    NSString* csv = extensionsCsv ? [NSString stringWithUTF8String:extensionsCsv] : @"zip,json";
    dispatch_async(dispatch_get_main_queue(), ^{
        @try {
            if (gPickerDelegate == nil) gPickerDelegate = [MediaTripPickerDelegate new];
            gPickerDelegate.exporting = NO;
            UIDocumentPickerViewController* picker = nil;
            NSArray<NSString*>* exts = [csv componentsSeparatedByString:@","];
#if __has_include(<UniformTypeIdentifiers/UniformTypeIdentifiers.h>)
            if (@available(iOS 14.0, *)) {
                NSMutableArray<UTType*>* types = [NSMutableArray array];
                for (NSString* e in exts) {
                    UTType* t = [UTType typeWithFilenameExtension:[e stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceCharacterSet]]];
                    if (t != nil) [types addObject:t];
                }
                if (types.count == 0) [types addObject:UTTypeData];
                // asCopy:NO hands us the provider's URL, so the security-scoped access above is real.
                picker = [[UIDocumentPickerViewController alloc] initForOpeningContentTypes:types asCopy:NO];
            }
#endif
            if (picker == nil) {
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
                picker = [[UIDocumentPickerViewController alloc] initWithDocumentTypes:@[@"public.zip-archive", @"public.json", @"public.data"] inMode:UIDocumentPickerModeOpen];
#pragma clang diagnostic pop
            }
            picker.delegate = gPickerDelegate;
            picker.allowsMultipleSelection = NO;
            if (@available(iOS 13.0, *)) picker.shouldShowFileExtensions = YES;
            MTPresent(picker);
        } @catch (NSException* ex) {
            MTSend("OnNativeError", [NSString stringWithFormat:@"Import picker threw: %@", ex.reason]);
        }
    });
}

void _MediaTrip_ExportFile(const char* path)
{
    NSString* p = path ? [NSString stringWithUTF8String:path] : @"";
    dispatch_async(dispatch_get_main_queue(), ^{
        @try {
            if (![[NSFileManager defaultManager] fileExistsAtPath:p]) { MTSend("OnNativeError", [NSString stringWithFormat:@"File to export does not exist: %@", p]); return; }
            if (gPickerDelegate == nil) gPickerDelegate = [MediaTripPickerDelegate new];
            gPickerDelegate.exporting = YES;
            NSURL* url = [NSURL fileURLWithPath:p];
            UIDocumentPickerViewController* picker = nil;
            if (@available(iOS 14.0, *)) {
                picker = [[UIDocumentPickerViewController alloc] initForExportingURLs:@[url] asCopy:YES];
            } else {
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
                picker = [[UIDocumentPickerViewController alloc] initWithURL:url inMode:UIDocumentPickerModeExportToService];
#pragma clang diagnostic pop
            }
            picker.delegate = gPickerDelegate;
            MTPresent(picker);
        } @catch (NSException* ex) {
            MTSend("OnNativeError", [NSString stringWithFormat:@"Export picker threw: %@", ex.reason]);
        }
    });
}

void _MediaTrip_ShareFile(const char* path)
{
    NSString* p = path ? [NSString stringWithUTF8String:path] : @"";
    dispatch_async(dispatch_get_main_queue(), ^{
        @try {
            if (![[NSFileManager defaultManager] fileExistsAtPath:p]) { MTSend("OnNativeError", [NSString stringWithFormat:@"File to share does not exist: %@", p]); return; }
            NSURL* url = [NSURL fileURLWithPath:p];
            UIActivityViewController* share = [[UIActivityViewController alloc] initWithActivityItems:@[url] applicationActivities:nil];
            share.completionWithItemsHandler = ^(UIActivityType activityType, BOOL completed, NSArray* returnedItems, NSError* activityError) {
                if (activityError != nil) MTSend("OnNativeError", [NSString stringWithFormat:@"Share failed: %@", activityError.localizedDescription]);
                else if (completed) MTSend("OnExportDone", activityType ?: @"shared");
                else MTSend("OnExportCancelled", @"cancel");
            };
            MTPresent(share);
        } @catch (NSException* ex) {
            MTSend("OnNativeError", [NSString stringWithFormat:@"Share sheet threw: %@", ex.reason]);
        }
    });
}

}
