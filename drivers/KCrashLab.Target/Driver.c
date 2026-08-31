#include <ntddk.h>
#include <wdf.h>
#include <wdmsec.h>
#include <initguid.h>
#include "Public.h"

DEFINE_GUID(GUID_DEVINTERFACE_KCRASHLAB_TARGET, 0x4fd15d37, 0x1f06, 0x4e50, 0xa8, 0x23, 0x37, 0x6a, 0xd4, 0x18, 0xf1, 0x96);

DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD KclEvtDeviceAdd;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL KclEvtIoDeviceControl;

typedef struct _KCL_DEVICE_CONTEXT {
    ULONG Mode;
    BOOLEAN ResetObserved;
} KCL_DEVICE_CONTEXT, *PKCL_DEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(KCL_DEVICE_CONTEXT, KclGetContext);

#if KCL_ENABLE_LAB_FAULTS
static const ULONG KclSyntheticBugCheck = 0xE2C1A501;
#endif

NTSTATUS DriverEntry(PDRIVER_OBJECT driverObject, PUNICODE_STRING registryPath)
{
    WDF_DRIVER_CONFIG config;
    WDF_DRIVER_CONFIG_INIT(&config, KclEvtDeviceAdd);
    return WdfDriverCreate(driverObject, registryPath, WDF_NO_OBJECT_ATTRIBUTES, &config, WDF_NO_HANDLE);
}

NTSTATUS KclEvtDeviceAdd(WDFDRIVER driver, PWDFDEVICE_INIT deviceInit)
{
    UNREFERENCED_PARAMETER(driver);
    DECLARE_CONST_UNICODE_STRING(deviceName, L"\\Device\\KCrashLabTarget");
    DECLARE_CONST_UNICODE_STRING(symbolicLink, L"\\DosDevices\\KCrashLabTarget");
    DECLARE_CONST_UNICODE_STRING(deviceSddl, SDDL_DEVOBJ_SYS_ALL_ADM_ALL);
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes, KCL_DEVICE_CONTEXT);
    WdfDeviceInitSetExclusive(deviceInit, TRUE);

    NTSTATUS status = WdfDeviceInitAssignName(deviceInit, &deviceName);
    if (!NT_SUCCESS(status)) return status;
    status = WdfDeviceInitAssignSDDLString(deviceInit, &deviceSddl);
    if (!NT_SUCCESS(status)) return status;

    WDFDEVICE device;
    status = WdfDeviceCreate(&deviceInit, &attributes, &device);
    if (!NT_SUCCESS(status)) return status;
    status = WdfDeviceCreateSymbolicLink(device, &symbolicLink);
    if (!NT_SUCCESS(status)) return status;
    status = WdfDeviceCreateDeviceInterface(device, &GUID_DEVINTERFACE_KCRASHLAB_TARGET, NULL);
    if (!NT_SUCCESS(status)) return status;

    PKCL_DEVICE_CONTEXT context = KclGetContext(device);
    context->Mode = 0;
    context->ResetObserved = FALSE;

    WDF_IO_QUEUE_CONFIG queueConfig;
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queueConfig, WdfIoQueueDispatchSequential);
    queueConfig.EvtIoDeviceControl = KclEvtIoDeviceControl;
    return WdfIoQueueCreate(device, &queueConfig, WDF_NO_OBJECT_ATTRIBUTES, WDF_NO_HANDLE);
}

VOID KclEvtIoDeviceControl(WDFQUEUE queue, WDFREQUEST request, size_t outputLength,
                           size_t inputLength, ULONG ioControlCode)
{
    UNREFERENCED_PARAMETER(outputLength);
    PKCL_DEVICE_CONTEXT context = KclGetContext(WdfIoQueueGetDevice(queue));
    NTSTATUS status = STATUS_INVALID_DEVICE_REQUEST;
    size_t information = 0;
    PVOID buffer = NULL;
    size_t bufferLength = 0;

    switch (ioControlCode) {
    case IOCTL_KCL_ECHO:
        if (inputLength == 0) {
            status = STATUS_SUCCESS;
            break;
        }
        status = WdfRequestRetrieveInputBuffer(request, inputLength, &buffer, &bufferLength);
        if (NT_SUCCESS(status)) {
            PVOID output = NULL;
            size_t outputBufferLength = 0;
            status = WdfRequestRetrieveOutputBuffer(request, inputLength, &output, &outputBufferLength);
            if (NT_SUCCESS(status)) {
                UNREFERENCED_PARAMETER(outputBufferLength);
                RtlCopyMemory(output, buffer, inputLength);
                information = inputLength;
            }
        }
        break;
    case IOCTL_KCL_RESET_STATE:
        context->Mode = 0;
        context->ResetObserved = TRUE;
        status = STATUS_SUCCESS;
        break;
    case IOCTL_KCL_SET_MODE:
        status = WdfRequestRetrieveInputBuffer(request, sizeof(ULONG), &buffer, &bufferLength);
        if (NT_SUCCESS(status)) context->Mode = *(PULONG)buffer;
        break;
    case IOCTL_KCL_SUBMIT_RECORD:
        status = WdfRequestRetrieveInputBuffer(request, sizeof(ULONG), &buffer, &bufferLength);
        if (NT_SUCCESS(status)) {
            PKCL_RECORD_INPUT record = (PKCL_RECORD_INPUT)buffer;
            const size_t payloadLength = bufferLength - FIELD_OFFSET(KCL_RECORD_INPUT, Payload);
#if KCL_ENABLE_LAB_FAULTS
            if (context->ResetObserved && context->Mode == 2 && record->DeclaredLength > payloadLength) {
                KeBugCheckEx(KclSyntheticBugCheck, record->DeclaredLength, payloadLength, context->Mode, 0);
            }
#endif
            status = record->DeclaredLength <= payloadLength ? STATUS_SUCCESS : STATUS_INVALID_BUFFER_SIZE;
        }
        break;
    default:
        break;
    }

    WdfRequestCompleteWithInformation(request, status, information);
}
