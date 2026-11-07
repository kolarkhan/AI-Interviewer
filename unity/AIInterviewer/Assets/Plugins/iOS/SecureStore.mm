// Keychain-backed secure storage for the Unity app (see Runtime/Platform/SecureStorage.cs).
// Items use kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly: not synced to iCloud, not restored to other devices.
// Requires Security.framework (added by Editor/IosBuildPostprocessor.cs).

#import <Foundation/Foundation.h>
#import <Security/Security.h>
#include <stdlib.h>
#include <string.h>

static NSString *const kAIIService = @"com.aiinterviewer.securestore";

static NSMutableDictionary *AIIBaseQuery(const char *key) {
    NSMutableDictionary *q = [NSMutableDictionary dictionary];
    q[(__bridge id)kSecClass] = (__bridge id)kSecClassGenericPassword;
    q[(__bridge id)kSecAttrService] = kAIIService;
    q[(__bridge id)kSecAttrAccount] = [NSString stringWithUTF8String:key];
    return q;
}

extern "C" {

bool AIISecureStoreSet(const char *key, const char *value) {
    if (key == NULL || value == NULL) return false;
    NSData *data = [[NSString stringWithUTF8String:value] dataUsingEncoding:NSUTF8StringEncoding];
    NSMutableDictionary *query = AIIBaseQuery(key);
    SecItemDelete((__bridge CFDictionaryRef)query);
    query[(__bridge id)kSecValueData] = data;
    query[(__bridge id)kSecAttrAccessible] = (__bridge id)kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly;
    OSStatus status = SecItemAdd((__bridge CFDictionaryRef)query, NULL);
    return status == errSecSuccess;
}

// Returns a malloc'ed UTF-8 string (free with AIISecureStoreFree) or NULL.
char *AIISecureStoreGet(const char *key) {
    if (key == NULL) return NULL;
    NSMutableDictionary *query = AIIBaseQuery(key);
    query[(__bridge id)kSecReturnData] = @YES;
    query[(__bridge id)kSecMatchLimit] = (__bridge id)kSecMatchLimitOne;
    CFTypeRef result = NULL;
    OSStatus status = SecItemCopyMatching((__bridge CFDictionaryRef)query, &result);
    if (status != errSecSuccess || result == NULL) return NULL;
    NSData *data = (__bridge_transfer NSData *)result;
    NSString *str = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
    if (str == nil) return NULL;
    const char *utf8 = [str UTF8String];
    char *copy = (char *)malloc(strlen(utf8) + 1);
    strcpy(copy, utf8);
    return copy;
}

bool AIISecureStoreDelete(const char *key) {
    if (key == NULL) return false;
    OSStatus status = SecItemDelete((__bridge CFDictionaryRef)AIIBaseQuery(key));
    return status == errSecSuccess || status == errSecItemNotFound;
}

void AIISecureStoreFree(char *ptr) {
    if (ptr != NULL) free(ptr);
}

}
