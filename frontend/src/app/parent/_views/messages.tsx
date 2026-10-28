"use client";

import type { GuardianMessage } from "@/lib/guardian";
import { EmptyCard, LoadError, MessageCard, SkeletonStack, ViewHeader } from "./shared";

export function MessagesView({ messages, loading, error, refetch }: {
  messages: GuardianMessage[] | undefined;
  loading: boolean;
  error: boolean;
  refetch: () => unknown;
}) {
  return (
    <div>
      <ViewHeader title="Mesajlar" subtitle="Okuldan gelen bildirimler" />
      {loading ? (
        <SkeletonStack heights={["h-24", "h-24", "h-24"]} />
      ) : error ? (
        <LoadError message="Mesajlar yüklenemedi" onRetry={refetch} />
      ) : messages?.length ? (
        <div className="space-y-2">{messages.map((message) => <MessageCard key={message.id} message={message} />)}</div>
      ) : (
        <EmptyCard icon="bell">Henüz bir bildirim yok.</EmptyCard>
      )}
    </div>
  );
}
