import type { BookingStatus, PaymentStatus } from '@/api/types'
import { bookingStatusLabel, paymentStatusLabel } from '@/lib/labels'
import { Badge } from './ui'

const statusTone = {
  Pending: 'warning', Confirmed: 'info', CheckedIn: 'success', CheckedOut: 'default', Cancelled: 'danger', NoShow: 'danger',
} as const satisfies Record<BookingStatus, Parameters<typeof Badge>[0]['tone']>

export const BookingStatusBadge = ({ status }: { status: BookingStatus }) =>
  <Badge tone={statusTone[status]}>{bookingStatusLabel[status]}</Badge>

export const PaymentBadge = ({ status }: { status: PaymentStatus }) =>
  <Badge tone={status === 'Paid' || status === 'PaidToChannel' ? 'success' : status === 'Unpaid' ? 'warning' : 'default'}>
    {paymentStatusLabel[status]}
  </Badge>
